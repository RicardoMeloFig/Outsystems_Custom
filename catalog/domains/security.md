# Security — reference routing

**Consult when**: roles and permissions, screen access, login/sign-in flows,
SAML/LDAP/Active Directory/Okta/SSO, session management, web security (XSS,
CSRF, CSP, WAF, HTTPS enforcement), view state encryption, data encryption
(HIPAA), OAuth/OpenID, identity provider integration.

## Primary sources

| Repository | Where to look | What you get |
|---|---|---|
| `docs-product` (O11) | `src/user-management/user-roles/` (`create-a-custom-role.md`, `validate-permissions.md`, `security-model/`), `src/user-management/end-user-manage/end-user-authentication/` (`configure-saml.md`, `configure-ldap.md`, `configure-active-directory.md`, `configure-okta.md`, `single-sign-on.md`), `src/security/` (`apply-content-security-policy.md`, `enforce-https-security.md`, `encrypt-viewstate.md`, `injection-XSS/`, `implementing-waf/`, `ext-rd-reactive-security-best-practices/`), `src/building-apps/data/hipaa/` | The official security model + hardening recipes (many are Reactive-app specific) |
| `docs-odc` | ODC security/user-model differences (secondary — O11-first workspace) | Where the model diverges |

## Secondary sources

| Repository | When |
|---|---|
| `keycloak-json-remote-claim` | Custom Keycloak claim mapper (identity federation) |
| `java-html-sanitizer` | Server-side HTML sanitization (XSS-safe rich input) |
| `cordova-outsystems-sociallogins`, `cordova-plugin-android-fingerprint-auth`, `cordova-plugin-touchid`, `cordova-plugin-secure-storage`, `TrustKit`, `capacitor-outsystems-sslpinning` | Mobile-side auth/secure storage/pinning — see `mobile.md` |

## Search tips

- `docs-product/toc.yml` "User management" and "Security" sections; filter
  pages by `app_type:` — Reactive screen permission enforcement differs from
  Traditional (best-practices doc exists for Reactive).
- O11 users are platform-managed (LifeTime users) — end-user auth is a
  per-app concern; read `end-user-manage/` before assuming.

## Related workspace skills

`app-security` (roles, Public flag, login gating).

## Gotchas

- Screen permissions are a UI-layer gate only — always enforce on the
  server logic too (business-layer checks).
- Never store secrets in site properties or screen inputs; use platform
  security configuration.
