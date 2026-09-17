#!/usr/bin/env bash
# =============================================================================
#  OutSystems Service Center - raw .oml downloader (SOAP / ServiceStudioSoap)
#
#  Verified against the SOAP action URIs embedded in OutSystems.Communication.dll
#  (Service Studio 11.0.415.100). The ServiceStudioSoap web service is the same
#  one Service Studio itself uses to talk to the platform server.
#
#  Endpoint :  http(s)://<host>/ServiceCenter/ws/ServiceStudioSoap
#  WSDL     :  http(s)://<host>/ServiceCenter/ws/ServiceStudioSoap?WSDL
#  Auth     :  HTTP Basic (Service Center username / password)
#  SOAPAction header = http://ServiceCenter/ServiceStudio/<Operation>
#
#  Key operations (all take username/password unless noted):
#    Handshake               - version compatibility check (do this first)
#    ListEspaces             - list modules (returns module keys/names)
#    ListExtensions          - list extensions
#    ListVersions            - list published versions of a module
#    GetESpaceId             - eSpaceKey -> numeric version id
#    Download                - download .oml by eSpaceKey + versionId  -> Byte[]
#    DownloadByName          - download .oml by module name
#    DownloadExtension       - download extension .oml by extensionKey + versionId
#    DownloadApplication     - download .oap application package
#    GetPublicElements       - list exposed/public elements of a module
#    GetReferenceDetails     - dependency graph for a module
#    TestQuery               - execute an Advanced SQL server-side (passes OML bytes!)
#    TestAction              - invoke a server action server-side
#    CanOpenOml              - licensing/product-key handshake (see C# loader)
#
#  NOTE: parameter names below (username/password/eSpaceKey/versionId) match the
#  IServiceCenter method signatures reflected from the DLL. If a call is rejected,
#  fetch the WSDL to confirm exact element names:
#    curl -u "$USER:$PASS" "$HOST/ServiceCenter/ws/ServiceStudioSoap?WSDL"
# =============================================================================
set -euo pipefail

: "${OS_HOST:?set OS_HOST, e.g. http://myserver or https://myserver:443}"
: "${OS_USER:?set OS_USER (Service Center username)}"
: "${OS_PASS:?set OS_PASS (Service Center password)}"

SOAP_URL="$OS_HOST/ServiceCenter/ws/ServiceStudioSoap"
NS="http://ServiceCenter/ServiceStudio"

soap_call() {
  local action="$1" body="$2"
  local envelope
  envelope=$(cat <<EOF
<?xml version="1.0" encoding="utf-8"?>
<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"
               xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
               xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <soap:Body>
    <${action} xmlns="${NS}">
${body}
    </${action}>
  </soap:Body>
</soap:Envelope>
EOF
)
  curl -sS --max-time 120 \
       -u "$OS_USER:$OS_PASS" \
       -H "Content-Type: text/xml; charset=utf-8" \
       -H "SOAPAction: \"${NS}/${action}\"" \
       -X POST "$SOAP_URL" \
       --data-raw "$envelope"
}

# --- 1. Handshake (verify auth + version compatibility) -----------------------
handshake() {
  echo ">>> Handshake ($OS_HOST)"
  soap_call Handshake "      <username>${OS_USER}</username>
      <password>${OS_PASS}</password>
      <version>11.0.415.100</version>
      <lastUpVersion>0</lastUpVersion>"
}

# --- 2. List modules (ListEspaces) --------------------------------------------
list_modules() {
  echo ">>> ListEspaces"
  soap_call ListEspaces "      <username>${OS_USER}</username>
      <password>${OS_PASS}</password>"
}

# --- 3. Download .oml by eSpaceKey + versionId --------------------------------
#  $1 = eSpaceKey  (the 22-char base64url from Parse-OmlHeader.ps1, field [3])
#  $2 = versionId  (numeric; 0 / omitted usually means "latest")
#  $3 = output file (default: <eSpaceKey>.oml)
download_oml() {
  local key="${1:?eSpaceKey required}"
  local ver="${2:-0}"
  local out="${3:-${key}.oml}"
  echo ">>> Download eSpaceKey=$key versionId=$ver -> $out"
  local resp
  resp=$(soap_call Download "      <username>${OS_USER}</username>
      <password>${OS_PASS}</password>
      <eSpaceKey>${key}</eSpaceKey>
      <versionId>${ver}</versionId>")
  # the Byte[] result is base64 text inside <DownloadResult>...</DownloadResult>
  local b64
  b64=$(printf '%s' "$resp" | sed -n 's/.*<DownloadResult>\(.*\)<\/DownloadResult>.*/\1/p')
  if [ -z "$b64" ]; then
    echo "ERROR: no DownloadResult in response. Raw response:" >&2
    printf '%s\n' "$resp" >&2
    return 1
  fi
  printf '%s' "$b64" | base64 -d > "$out"
  echo "Saved $(wc -c < "$out") bytes -> $out"
  echo "Header check:"
  command -v powershell >/dev/null 2>&1 && \
    powershell -NoProfile -ExecutionPolicy Bypass -File \
      "$(dirname "$0")/Parse-OmlHeader.ps1" -Path "$out" 2>/dev/null || true
}

# --- 4. Download by name (no eSpaceKey needed) --------------------------------
download_by_name() {
  local name="${1:?module name required}"
  local out="${2:-${name}.oml}"
  echo ">>> DownloadByName name=$name -> $out"
  local resp
  resp=$(soap_call DownloadByName "      <username>${OS_USER}</username>
      <password>${OS_PASS}</password>
      <name>${name}</name>")
  local b64
  b64=$(printf '%s' "$resp" | sed -n 's/.*<DownloadByNameResult>\(.*\)<\/DownloadByNameResult>.*/\1/p')
  [ -z "$b64" ] && { echo "ERROR: empty result"; printf '%s\n' "$resp" >&2; return 1; }
  printf '%s' "$b64" | base64 -d > "$out"
  echo "Saved $(wc -c < "$out") bytes -> $out"
}

# --- usage --------------------------------------------------------------------
usage() {
  cat <<EOF
Usage: OS_HOST=... OS_USER=... OS_PASS=... $0 <command> [args]

  handshake                          check auth + version compat
  list-modules                       list all modules (keys + names)
  download <eSpaceKey> [ver] [out]   download .oml by module key
  download-name <name> [out]         download .oml by module name

Example:
  export OS_HOST=https://acme.outsystems.net OS_USER=admin OS_PASS=secret
  $0 handshake
  $0 list-modules
  $0 download 9WKxOWxj2EqnjVqbq6dV_Q 0 AdminTool.oml
EOF
}

case "${1:-}" in
  handshake)        handshake ;;
  list-modules)     list_modules ;;
  download)         shift; download_oml "$@" ;;
  download-name)    shift; download_by_name "$@" ;;
  *)                usage; exit 1 ;;
esac
