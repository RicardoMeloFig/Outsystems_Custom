def call(Map cfg = [:]) {
  String tokenUrl = (cfg.tokenUrl ?: env.TOKEN_URL)?.trim()
  String baseUrl  = (cfg.baseUrl  ?: env.BASE_URL)?.trim()
  String assetKey = (cfg.assetKey ?: env.ASSET_KEY ?: env.APP_KEY)?.trim()
  String envKey   = (cfg.environmentKey ?: cfg.envKey ?: env.ENVIRONMENT_KEY ?: env.QA_ENVIRONMENT_KEY ?: env.PRD_ENVIRONMENT_KEY ?: '').trim()
  String revision = (cfg.revision ?: env.REVISION_SELECTED ?: '').toString().trim()
  String buildKey = (cfg.buildKey ?: env.BUILD_KEY ?: '').toString().trim()

  if (!tokenUrl || !baseUrl || !assetKey || !envKey || !revision || !buildKey) {
    error "deployToEnv: missing required inputs (tokenUrl/baseUrl/assetKey/environmentKey/revision/buildKey)"
  }

  String oauthCredsId = (cfg.oauthCredentialsId ?: 'oauth-client') as String
  int overallTO       = (cfg.overallTimeoutMinutes ?: 60) as int
  int noChangeM       = (cfg.noChangeMinutes       ?: 10) as int
  String artifact     = (cfg.artifactFile          ?: 'deployment-result.json') as String
  String operation    = (cfg.operation             ?: 'Deploy') as String

  def revValue = (revision ==~ /^\d+$/) ? Integer.parseInt(revision) : revision

  // === 1) Token via reusable helper ===
  def tok = getOAuthToken(tokenUrl: tokenUrl, oauthCredentialsId: oauthCredsId, scope: cfg.scope ?: '')
  String token = tok.access_token as String

  // === 2) Trigger deploy ===
  final String deployUrl = "${baseUrl}/api/deployments/v1/deployment-operations"
  def bodyMap  = [
    operation     : operation,   // "Deploy"
    assetKey      : assetKey,
    buildKey      : buildKey,
    revision      : revValue,
    environmentKey: envKey
  ]
  def bodyJson = writeJSON returnText: true, json: bodyMap

  def deployResp = httpRequest(
    url: deployUrl,
    httpMode: 'POST',
    contentType: 'APPLICATION_JSON',
    acceptType: 'APPLICATION_JSON',
    customHeaders: [[name: 'Authorization', value: "Bearer ${token}", maskValue: true]],
    requestBody: bodyJson,
    validResponseCodes: '200:299',
    timeout: 60,
    consoleLogResponseBody: true
  )
  def dj        = readJSON text: deployResp.content
  final String depKey    = (dj?.key    ?: '').toString()
  String       depStatus = (dj?.status ?: '').toString()
  String       depOp     = (dj?.operation ?: '').toString()

  if (!depKey) error "[deployToEnv] No deployment key returned by POST ${deployUrl}"
  if (depOp && !depOp.equalsIgnoreCase(operation)) {
    error "[deployToEnv] Unexpected operation in response: '${depOp}' (expected: '${operation}')"
  }

  echo "[deployToEnv] Deployment created key=${depKey}, initial status=${depStatus}, op=${depOp ?: operation}"

  // Immediate policy: Finished=ok, Running=continue, else=fail
  if (depStatus.equalsIgnoreCase('Finished')) {
    echo "[deployToEnv] Deployment already Finished."
    writeJSON file: artifact,
             json: [ deploymentKey: depKey, environmentKey: envKey, assetKey: assetKey, revision: revision, buildKey: buildKey, finalStatus: depStatus ],
             pretty: 4
    archiveArtifacts artifacts: artifact, fingerprint: true
    return [deploymentKey: depKey, status: depStatus]
  } else if (!depStatus.equalsIgnoreCase('Running')) {
    error "Deployment ended with status: ${depStatus}"
  }

  // === 3) Poll until finished ===
  final String statusUrl   = "${baseUrl}/api/deployments/v1/deployment-operations/${depKey}"
  final long   startMs     = System.currentTimeMillis()
  long         lastChange  = startMs
  String       lastStatus  = depStatus
  final long   maxNoChange = noChangeM * 60 * 1000L

  def nextInterval = { long elapsedSec ->
    if (elapsedSec < 30)      return 5
    else if (elapsedSec < 60) return 15
    else                      return 30
  }

  timeout(time: overallTO, unit: 'MINUTES') {
    waitUntil {
      def stResp = httpRequest(
        url: statusUrl,
        httpMode: 'GET',
        acceptType: 'APPLICATION_JSON',
        customHeaders: [[name: 'Authorization', value: "Bearer ${token}", maskValue: true]],
        validResponseCodes: '200:299',
        timeout: 60,
        consoleLogResponseBody: true
      )
      def sj     = readJSON text: stResp.content
      def status = (sj?.status ?: '').toString()
      echo "[deployToEnv] Deployment status: ${status}"

      if (status.equalsIgnoreCase('Finished')) {
        echo "[deployToEnv] Deployment finished successfully."
        true
      } else if (status.equalsIgnoreCase('Running')) {
        long now = System.currentTimeMillis()
        if (!status.equalsIgnoreCase(lastStatus)) {
          lastStatus = status
          lastChange = now
        }
        if ((now - lastChange) >= maxNoChange) {
          error "Deployment status '${status}' unchanged for ${noChangeM} minutes. Aborting."
        }
        int elapsedSec = ((now - startMs) / 1000L) as int
        int waitSec    = nextInterval(elapsedSec)
        sleep time: waitSec, unit: 'SECONDS'
        false
      } else {
        error "Deployment ended with status: ${status}"
      }
    }
  }

  writeJSON file: artifact,
           json: [ deploymentKey: depKey, environmentKey: envKey, assetKey: assetKey, revision: revision, buildKey: buildKey, finalStatus: 'Finished' ],
           pretty: 4
  archiveArtifacts artifacts: artifact, fingerprint: true

  return [deploymentKey: depKey, status: 'Finished']
}
