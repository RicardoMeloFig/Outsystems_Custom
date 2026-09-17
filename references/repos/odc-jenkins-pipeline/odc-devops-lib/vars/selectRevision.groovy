def call(Map cfg = [:]) {
  String tokenUrl = (cfg.tokenUrl ?: env.TOKEN_URL)?.trim()
  String baseUrl  = (cfg.baseUrl  ?: env.BASE_URL)?.trim()
  String assetKey = (cfg.assetKey ?: env.ASSET_KEY ?: env.APP_KEY)?.trim()

  String revTpl = (cfg.revisionsPathTemplate
                  ?: env.REVISIONS_PATH_TEMPLATE
                  ?: '/api/asset-repository/v1/assets/%s/revisions').trim()

  String bldTpl = (cfg.buildsQueryTemplate
                  ?: env.BUILDS_QUERY_TEMPLATE
                  ?: env.BUILDS_PATH_TEMPLATE
                  ?: '/api/builds/v1/build-operations?assetKey=%s&assetRevision=%s&byBuildType=Release').trim()

  if (!tokenUrl || !baseUrl || !assetKey) {
    error "selectRevision: missing required inputs (tokenUrl/baseUrl/assetKey)"
  }

  String oauthCredsId = (cfg.oauthCredentialsId ?: 'oauth-client') as String
  String submitters   = (cfg.submitters ?: env.SUBMITTERS ?: '') as String
  int timeoutMinutes  = (cfg.timeoutMinutes ?: 0) as int
  boolean setEnv      = (cfg.containsKey('setEnv') ? cfg.setEnv : true) as boolean
  Map extraHeaders    = (cfg.extraHeaders ?: [:]) as Map

  def enc = { s -> java.net.URLEncoder.encode(s ?: '', 'UTF-8') }
  def listUrlFor   = { key -> "${baseUrl}${String.format(revTpl, enc(key))}" }
  def buildsUrlFor = { key, rev -> "${baseUrl}${String.format(bldTpl, enc(key), enc(rev))}" }
  def userUrlFor   = { userKey -> "${baseUrl}/api/identity/v1/users/${enc(userKey)}" }

  // === 1) Token via reusable helper ===
  def tok = getOAuthToken(tokenUrl: tokenUrl, oauthCredentialsId: oauthCredsId, scope: cfg.scope ?: '')
  String token = tok.access_token as String

  // === 2) List revisions ===
  String listUrl = listUrlFor(assetKey)
  def baseAuthHeader = [[name: 'Authorization', value: "Bearer ${token}", maskValue: true]]
  def listHeaders = baseAuthHeader + extraHeaders.collect { k, v -> [name: k.toString(), value: (v as String)] }

  def listResp = httpRequest(
    url: listUrl,
    httpMode: 'GET',
    acceptType: 'APPLICATION_JSON',
    customHeaders: listHeaders,
    validResponseCodes: '200:299',
    timeout: 60,
    consoleLogResponseBody: true
  )

  def payload = readJSON text: listResp.content
  List items = (payload?.results instanceof List) ? payload.results : (payload instanceof List ? payload : [])
  if (!items || items.isEmpty()) error "No revisions returned for assetKey=${assetKey}"

  // === 2a) Resolve user names for revisionUserKey via Identity API ===
  def nameCache = [:] as Map<String,String>
  def userKeys = items.collect { (it?.revisionUserKey ?: '').toString() }.findAll { it }.unique()

  userKeys.each { userKey ->
    try {
      def uResp = httpRequest(
        url: userUrlFor(userKey),
        httpMode: 'GET',
        acceptType: 'APPLICATION_JSON',
        customHeaders: baseAuthHeader,     // usually no extra headers needed here
        validResponseCodes: '200:299,404',
        timeout: 60,
        consoleLogResponseBody: false
      )
      if (uResp.status == 200 && uResp.content?.trim()) {
        def uj = readJSON text: uResp.content
        // Try common name fields; fall back to email, then key
        def disp = (uj?.name ?: uj?.fullName ?: uj?.displayName ?: uj?.email ?: userKey).toString()
        nameCache[userKey] = disp
      } else {
        nameCache[userKey] = userKey
      }
    } catch (Throwable t) {
      echo "[selectRevision] WARN: Failed to resolve user '${userKey}': ${t.message}"
      nameCache[userKey] = userKey
    }
  }

  // === 3) Build dropdown labels (show user NAME instead of key) ===
  def byLabel = [:]
  for (def entry : items) {
    if (!(entry instanceof Map)) continue
    def rev   = entry.revision?.toString()
    def uKey  = entry.revisionUserKey?.toString()
    def uName = uKey ? (nameCache[uKey] ?: uKey) : ''
    def when  = entry.revisionDateTime?.toString()
    def name  = entry.name?.toString()
    if (rev) {
      def label = "${rev}" +
                  (uName ? " | by=${uName}" : "") +
                  (when  ? " | when=${when}" : "") +
                  (name  ? " | asset name=${name}" : "")
      byLabel[label] = [revision: rev, userKey: uKey, userName: uName, when: when]
    }
  }
  if (byLabel.isEmpty()) error "Could not map revisions from list response."

  // === 4) Prompt ===
  def choicesStr = byLabel.keySet().join('\n')
  def params = [ choice(name: 'REVISION_CHOICE', choices: choicesStr, description: "Select revision for assetKey=${assetKey}") ]

  def selection
  if (timeoutMinutes > 0) {
    timeout(time: timeoutMinutes, unit: 'MINUTES') {
      selection = (submitters?.trim())
        ? input(message: 'Choose revision to promote', parameters: params, submitter: submitters)
        : input(message: 'Choose revision to promote', parameters: params)
    }
  } else {
    selection = (submitters?.trim())
      ? input(message: 'Choose revision to promote', parameters: params, submitter: submitters)
      : input(message: 'Choose revision to promote', parameters: params)
  }

  def chosen = byLabel[selection as String]
  if (!chosen) error "Invalid selection '${selection}'."
  String chosenRev  = chosen.revision as String
  String chosenName = chosen.userName as String
  String chosenKey  = chosen.userKey as String

  // === 5) Get builds for chosen revision ===
  String buildsUrl = buildsUrlFor(assetKey, chosenRev)
  def buildsHeaders = baseAuthHeader + extraHeaders.collect { k, v -> [name: k.toString(), value: (v as String)] }

  def bResp = httpRequest(
    url: buildsUrl,
    httpMode: 'GET',
    acceptType: 'APPLICATION_JSON',
    customHeaders: buildsHeaders,
    validResponseCodes: '200:299,404',
    timeout: 60,
    consoleLogResponseBody: true
  )

  String buildKey = ''
  if (bResp.status == 200 && bResp.content?.trim()) {
    def bj = readJSON text: bResp.content
    List builds = (bj?.builds instanceof List) ? bj.builds : []
    if (builds && !builds.isEmpty()) {
      def ts = { Map b -> b.finishedDateTime ?: b.startedDateTime ?: '' }
      builds.sort { a, b -> (ts(b) ?: '') <=> (ts(a) ?: '') }
      buildKey = (builds[0]?.buildKey ?: '').toString()
    } else {
      echo "[selectRevision] WARN: No builds returned for revision ${chosenRev}"
    }
  } else {
    echo "[selectRevision] WARN: Build details not found for revision ${chosenRev} (HTTP ${bResp.status})."
  }

  // === 6) Store / return ===
  def result = [
    assetKey         : assetKey,
    revision         : chosenRev,
    buildKey         : buildKey,
    revisionUserKey  : chosenKey ?: '',
    revisionUserName : chosenName ?: '',
    revisionDateTime : chosen.when ?: '',
    listUrl          : listUrl,
    buildsUrl        : buildsUrl
  ]

  if (setEnv) {
    env.REVISION_SELECTED = result.revision
    env.REV_USER_KEY      = result.revisionUserKey
    env.REV_USER_NAME     = result.revisionUserName
    env.REV_DATE_TIME     = result.revisionDateTime
    env.BUILD_KEY         = result.buildKey
    
    echo "Asset key for this run: ${assetKey}"

    currentBuild.displayName = "rev=${env.REVISION_SELECTED} • #${env.BUILD_NUMBER}"
    currentBuild.description = "Revision=${env.REVISION_SELECTED}" +
                               (env.REV_USER_NAME ? " • By=${env.REV_USER_NAME}" : "") +
                               (env.REV_DATE_TIME ? " • When=${env.REV_DATE_TIME}" : "") +
                               (env.BUILD_KEY ? " • BuildKey=${env.BUILD_KEY}" : "")

    writeJSON file: 'selection.json', pretty: 4, json: [
      assetKey          : assetKey,
      revision          : result.revision,
      revisionUserKey   : result.revisionUserKey,
      revisionUserName  : result.revisionUserName,
      revisionDateTime  : result.revisionDateTime,
      buildKey          : result.buildKey,
      listUrl           : listUrl,
      buildsUrl         : buildsUrl
    ]
    archiveArtifacts artifacts: 'selection.json', fingerprint: true
  }

  return result
}
