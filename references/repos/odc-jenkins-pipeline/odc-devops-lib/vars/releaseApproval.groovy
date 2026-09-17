def call(Map cfg = [:]) {
  String tokenUrl = (cfg.tokenUrl ?: env.TOKEN_URL)?.trim()
  String baseUrl  = (cfg.baseUrl  ?: env.BASE_URL)?.trim()
  String assetKey = (cfg.assetKey ?: env.ASSET_KEY)?.trim()
  String revision = (cfg.revision ?: env.REVISION_SELECTED ?: '').toString().trim()

  if (!tokenUrl || !baseUrl || !assetKey || !revision) {
    error "releaseApproval: missing required inputs (tokenUrl/baseUrl/assetKey/revision)"
  }

  String credId     = (cfg.oauthCredentialsId ?: 'oauth-client') as String
  String submitters = (cfg.submitters ?: env.SUBMITTERS_PRD ?: env.SUBMITTERS ?: '') as String
  String artifact   = (cfg.artifactFile ?: 'release.json') as String
  boolean debugLog  = (cfg.containsKey('debug') ? cfg.debug : false) as boolean

  // --- helpers ---
  def dbg = { String msg -> if (debugLog) echo "[releaseApproval][debug] ${msg}" }
  def hexDump = { String s ->
    if (s == null) return ''
    s.toCharArray().collect { ch -> 'U+' + Integer.toHexString((int) ch).toUpperCase().padLeft(4, '0') }.join(' ')
  }
  // remove CR/LF/TAB, Unicode spaces, common zero-width chars
  def cleanVersion = { String s ->
    (s ?: '')
      .replaceAll(/[\r\n\t]+/, '')
      .replaceAll(/\p{Z}+/, '')
      .replaceAll(/[\u200B-\u200D\u2060\uFEFF]/, '')
      .trim()
  }

  // === OAuth token via reusable helper ===
  def tok = getOAuthToken(tokenUrl: tokenUrl, oauthCredentialsId: credId, scope: cfg.scope ?: '', echoInfo: !debugLog)
  String token = tok.access_token as String
  def authHeader = [[name: 'Authorization', value: "Bearer ${token}", maskValue: true]]

  // === 1) Highest-tag-revision => suggest next version (bump PATCH) ===
  String highestUrl = "${baseUrl}/api/asset-repository/v1/assets/${assetKey}/highest-tag-revision"
  String currentTag = ''
  Integer taggedRev = null
  try {
    def hResp = httpRequest(
      url: highestUrl,
      httpMode: 'GET',
      acceptType: 'APPLICATION_JSON',
      customHeaders: authHeader,
      validResponseCodes: '200:299,404',
      timeout: 60,
      consoleLogResponseBody: debugLog
    )
    if (hResp.status == 200 && hResp.content?.trim()) {
      def hj = readJSON text: hResp.content
      currentTag = (hj?.tag ?: '').toString()
      taggedRev  = (hj?.revision ?: null) as Integer
    }
  } catch (Throwable t) {
    echo "[releaseApproval] WARN: highest-tag-revision lookup failed: ${t.message}"
  }

  def suggestNext = { String tag ->
    def m = (tag ?: '').trim() =~ /^(\d+)\.(\d+)\.(\d+)$/
    if (m.matches()) {
      int maj = m[0][1] as int
      int min = m[0][2] as int
      int pat = m[0][3] as int
      return "${maj}.${min}.${pat + 1}"
    }
    return "0.0.1"
  }
  String proposed = suggestNext(currentTag)

  dbg "currentTag='${currentTag}' len=${currentTag?.length()} codes=[${hexDump(currentTag)}]"
  dbg "proposed='${proposed}' len=${proposed.length()} codes=[${hexDump(proposed)}] matches=${(proposed ==~ /^\d+\.\d+\.\d+$/)}"

  // === 2) Manual approval + inputs (no timeout) ===
  def params = [
    string(
      name: 'RELEASE_VERSION',
      defaultValue: proposed,
      description: "Release version (MAJOR.MINOR.PATCH). Highest: '${currentTag ?: 'none'}' at revision ${taggedRev ?: 'n/a'}"
    ),
    text(
      name: 'RELEASE_NOTES',
      defaultValue: '',
      description: "Release notes for revision ${revision}"
    )
  ]
  def answers = (submitters?.trim())
    ? input(message: 'Production release approval', parameters: params, submitter: submitters)
    : input(message: 'Production release approval', parameters: params)

  String relVersionRaw = (answers?.RELEASE_VERSION)?.toString()
  String relVersion    = cleanVersion(relVersionRaw)
  String relNotes      = (answers?.RELEASE_NOTES ?: '').toString()

  dbg "relVersionRaw='${relVersionRaw}' len=${relVersionRaw?.length()} codes=[${hexDump(relVersionRaw)}]"
  dbg "relVersion   ='${relVersion}'    len=${relVersion.length()} codes=[${hexDump(relVersion)}]"
  dbg "relVersion matches? ${(relVersion ==~ /^\d+\.\d+\.\d+$/)}"

  // === 2a) Validate MAJOR.MINOR.PATCH ===
  if (!(relVersion ==~ /^\d+\.\d+\.\d+$/)) {
    error "Invalid version '${relVersion}'. Expected MAJOR.MINOR.PATCH (e.g., 1.2.3)."
  }

  // === 3) PATCH tag + release notes to the selected revision ===
  String patchUrl = "${baseUrl}/api/asset-repository/v1/assets/${assetKey}/revisions/${revision}"
  def body = [ tag: relVersion, releaseNotes: relNotes ]
  def bodyJson = writeJSON returnText: true, json: body

  def pResp = httpRequest(
    url: patchUrl,
    httpMode: 'PATCH',
    contentType: 'APPLICATION_JSON',
    acceptType: 'APPLICATION_JSON',
    customHeaders: authHeader,
    requestBody: bodyJson,
    validResponseCodes: '200:299',
    timeout: 60,
    consoleLogResponseBody: debugLog
  )

  echo "[releaseApproval] Tagged revision ${revision} with '${relVersion}'."

  // export for later stages + artifact
  env.RELEASE_VERSION = relVersion
  env.RELEASE_NOTES   = relNotes

  writeJSON file: artifact,
           json: [
             assetKey        : assetKey,
             revision        : revision,
             highestTag      : currentTag ?: null,
             highestTagRev   : taggedRev,
             releaseVersion  : relVersion,
             releaseNotes    : relNotes,
             highestUrl      : highestUrl,
             patchUrl        : patchUrl
           ],
           pretty: 4
  archiveArtifacts artifacts: artifact, fingerprint: true

  return [releaseVersion: relVersion, releaseNotes: relNotes, patchedRevision: revision]
}
