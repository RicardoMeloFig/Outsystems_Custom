# OutSystems ODC CI/CD Accelerators for Jenkins

[![Status](https://img.shields.io/badge/status-community-blue.svg)](#disclaimer)
[![Jenkins](https://img.shields.io/badge/jenkins-pipeline-orange.svg)](https://www.jenkins.io/)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

A small set of **Jenkins pipeline helpers** and an example **Jenkinsfile** to integrate with **OutSystems Developer Cloud (ODC) REST APIs**.  
They orchestrate: **Select Revision → Deploy to QA → Approval → Deploy to Staging → Release Approval (Version & Notes) → Deploy to Production**, using OAuth2 Client Credentials and robust status polling.

> These are **accelerators** (community samples), **not** product-supported. Fork and adapt to your governance.  
> Official ODC REST API docs: https://success.outsystems.com/documentation/outsystems_developer_cloud/odc_rest_apis/

---

## Repository contents
- `vars/getOAuthToken.groovy` — OAuth2 Client Credentials (client_id/secret in form body)
- `vars/selectRevision.groovy` — List revisions, resolve author name, pick one, fetch latest Release build
- `vars/deployToEnv.groovy` — POST Deploy operation + smart polling/back-off
- `vars/releaseApproval.groovy` — Suggest next SemVer, capture Release Version & Notes, PATCH the revision
- `Jenkinsfile` — Example pipeline wiring: Select → QA → Staging → Release approval → Prod
- `LICENSE`
---

## What the pipeline does

1. **Select revision** for an asset  
   Lists revisions, resolves **author name** via Identity API, prompts a user to choose, finds the latest **Release** build.
2. **Deploy to QA** and **poll** until `Finished`  
   Back-off: 5s → 15s → 30s; watchdog abort if status unchanged for 10 min.
3. **Manual approval**: confirm QA passed
4. **Deploy to Staging** and poll
5. **Release approval (Version & Notes)**  
   Suggest next SemVer (bump patch) via “highest tag”, prompt for **Release Version** and **Release Notes**, validate and **PATCH** the revision.
6. **Deploy to Production** and poll

Each step emits small JSON artifacts for auditing: `selection.json`, `release.json`, and `deployment-result-*.json`.

---

## Jenkins prerequisites

Install these plugins (Manage Jenkins → Plugins):

- **Pipeline (workflow-aggregator):** Declarative/Scripted Pipeline engine and core steps.  
- **Credentials Binding:** Safely exposes credentials to pipeline steps.  
- **HTTP Request:** Provides the `httpRequest` step for REST calls.  
- **Pipeline Utility Steps:** `readJSON`, `writeJSON`, archiving, etc.  
- **Timestamper:** Timestamps in console output (handy for polling analysis).  
- **Blue Ocean (optional):** Nicer visualization of stages and approvals.

Restart Jenkins after installing.

---

## Required credentials & variables

**Credentials (Global):**
- **ID:** `oauth-client` (Kind: *Username with password*)  
  **Username:** your OAuth `client_id`  
  **Password:** your OAuth `client_secret`  
  These are sent in the **token request body** (`application/x-www-form-urlencoded`).

**Job → Configure → Pipeline → Environment:**
- `BASE_URL`  → e.g. `https://host.outsystems.dev`  
- `TOKEN_URL` → e.g. `https://host.outsystems.dev/auth/realms/<realm>/protocol/openid-connect/token`  
- `ASSET_KEY` → asset/app GUID to promote  
- `QA_ENVIRONMENT_KEY`, `STAGING_ENVIRONMENT_KEY`, `PRD_ENVIRONMENT_KEY` → environment GUIDs  
- `SUBMITTERS` *(optional)* → comma-separated users allowed to approve

> Note: `BASE_URL` and `TOKEN_URL` usually share the same host; `TOKEN_URL` includes the realm OIDC path.

---

## How to use

1) **Register as a Global Pipeline Library**  
   Manage Jenkins → Configure System → **Global Pipeline Libraries**  
   • Name: `odc-devops-lib` • Retrieval: Git (Modern SCM) → this repo fork URL

2) **Create a Pipeline job**  
   New Item → **Pipeline** → paste the example `Jenkinsfile` (or load from SCM).

3) **Configure environment variables**  
   Set the variables above to match your tenant, asset, and environment GUIDs.

4) **Run the job**  
   Select a revision when prompted, approve QA/Prod gates, and provide Release Version & Notes before Production.  
   When changing the library, **start a new build** (don’t use “Restart from Stage”) to load the latest code.

---

## Library reference 

- **`getOAuthToken(Map cfg)`** → `{ access_token, expires_in }`  
  Gets an access token via Client Credentials with `client_id`/`client_secret` in the body.

- **`selectRevision(Map cfg)`** → returns a map and sets env vars  
  Lists recent revisions for `ASSET_KEY`, resolves author names, prompts the user to pick one, then finds the latest **Release** build.  
  Sets: `REVISION_SELECTED`, `BUILD_KEY`, `REV_USER_NAME`, `REV_DATE_TIME`.  
  Artifact: `selection.json`.

- **`deployToEnv(Map cfg)`** → `{ deploymentKey, status }`  
  Triggers a deploy to the given environment key and polls status until **Finished**; continues on **Running**; fails otherwise.  
  Configurable back-off, overall timeout, and a no-change watchdog.  
  Artifact: `deployment-result-*.json`.

- **`releaseApproval(Map cfg)`** → `{ releaseVersion, releaseNotes, patchedRevision }`  
  Suggests next SemVer (patch bump), prompts for **Release Version** and **Release Notes**, validates `MAJOR.MINOR.PATCH`, and stores them on the revision.  
  Sets: `RELEASE_VERSION`, `RELEASE_NOTES`.  
  Artifact: `release.json`.

All steps obtain tokens via `getOAuthToken` and attach the proper Authorization header.

---

## Troubleshooting

- **“Missing or invalid authentication”**  
  Ensure the token is issued for the same host/realm as `BASE_URL`. The steps add `Authorization: Bearer <token>` automatically.

- **“Scripts not permitted …”**  
  Confirm **Pipeline**, **HTTP Request**, and **Pipeline Utility Steps** are installed; restart Jenkins. Use the shared-library steps (sandbox-safe).

- **Version rejected in Release Approval**  
  Use strict SemVer `MAJOR.MINOR.PATCH` (e.g., `1.2.3`). Type it manually to avoid hidden characters.

- **Pipeline didn’t pick up library changes**  
  Start a **new run** so Jenkins loads the latest library revision.

---

## Security & governance

- Store secrets in **Jenkins Credentials**; never commit them.  
- Limit approvers via `SUBMITTERS`.  
- Keep emitted JSON artifacts for audits and change tracking.  
- Align gates with your change-management policy.

---

## Extending to other CI/CD tools

The OutSystems API flow is CI/CD-agnostic. Recreate these steps in Azure DevOps, GitHub Actions, or GitLab:
- Obtain token (Client Credentials)
- List revisions → choose one → get latest **Release** build
- Post a **Deploy** and poll status
- Optional: tag revision with **Release Version** and **Notes** before Production

---

## Contributing

Issues and PRs are welcome (improvements, docs, fixes).  
Please don’t include secrets, tenant IDs, or customer-identifying information.

---

## Disclaimer

This repository is provided **as-is** without warranty and is **not officially supported** by OutSystems. Scripts were created with AI support and reviewed by humans. Use at your own risk and adapt to your organization’s security and compliance requirements.

---

## License

This project is licensed under the **MIT License** – see [LICENSE](LICENSE).
