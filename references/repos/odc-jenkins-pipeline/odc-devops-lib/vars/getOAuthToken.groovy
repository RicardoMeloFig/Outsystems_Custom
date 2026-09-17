def call(Map cfg = [:]) {
  /*
    Required:
      tokenUrl            (String)  - Keycloak/OIDC token endpoint

    Optional:
      oauthCredentialsId  (String)  = 'oauth-client'  // Jenkins Username+Password (client_id / client_secret)
      scope               (String)  = ''              // optional scope
      extraForm           (Map)     = [:]             // extra form fields to append
      echoInfo            (boolean) = true            // log a tiny message with expires_in

    Returns:
      [ access_token: String, expires_in: Integer|null ]
  */
  String tokenUrl = (cfg.tokenUrl ?: env.TOKEN_URL)?.trim()
  if (!tokenUrl) error "getOAuthToken: tokenUrl is required (pass tokenUrl or set env.TOKEN_URL)"

  String credId  = (cfg.oauthCredentialsId ?: 'oauth-client') as String
  String scope   = (cfg.scope ?: '') as String
  Map extraForm  = (cfg.extraForm ?: [:]) as Map
  boolean echoIt = (cfg.containsKey('echoInfo') ? cfg.echoInfo : true) as boolean

  String token
  Integer expiresIn = null

  withCredentials([usernamePassword(credentialsId: credId, usernameVariable: 'CID', passwordVariable: 'CSECRET')]) {
    def enc   = { s -> java.net.URLEncoder.encode(s ?: '', 'UTF-8') }
    def pairs = [
      "grant_type=client_credentials",
      "client_id=${enc(env.CID)}",
      "client_secret=${enc(env.CSECRET)}"
    ]
    if (scope) pairs << "scope=${enc(scope)}"
    extraForm.each { k, v ->
      if (k && v != null) pairs << "${enc(k.toString())}=${enc(v.toString())}"
    }
    def form = pairs.join('&')

    def resp = httpRequest(
      url: tokenUrl,
      httpMode: 'POST',
      contentType: 'APPLICATION_FORM',
      acceptType: 'APPLICATION_JSON',
      requestBody: form,
      validResponseCodes: '200:299',
      timeout: 60,
      consoleLogResponseBody: false
    )
    def json = readJSON text: resp.content
    if (!json?.access_token) error "getOAuthToken: token endpoint did not return access_token (HTTP ${resp.status})"
    token     = json.access_token as String
    expiresIn = (json.expires_in ?: null) as Integer
  }

  if (echoIt) echo "[getOAuthToken] Got access token (expires_in=${expiresIn})"
  return [ access_token: token, expires_in: expiresIn ]
}
