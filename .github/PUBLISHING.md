# Package publishing

Pixely is packed once for every `main` commit. The resulting package is published immediately to the development feed and can later be published unchanged to nuget.org once its platform validation has passed.

## Development feed

Pull requests run the complete build, test, integration, NativeAOT, and package-consumer slice on Linux ARM64. After merge, the **Publish to development feed** workflow packs the commit and publishes the `.nupkg` through Sleet as soon as the pack finishes, storing the matching `.snupkg` and checksum manifest under `promotion/pixely/<version>/` in the R2 bucket. In parallel, it runs the full validation on Linux x64, Windows x64, and macOS ARM64 and validates the canonical package through isolated consumers on those three platforms. A development version can therefore be on the feed while its validation is still running or has failed. Publication attempts are serialized, reruns reuse the first stored files, and existing content is never overwritten.

The merged pull request receives two comments. The publish job posts the version and a direct package download link as soon as the package is on the feed. When the whole run completes, the **Report development publication** workflow posts the platform validation result, naming the failed jobs, and a link to the run. Each rerun is reported separately. Both use the temporary repository-scoped `GITHUB_TOKEN`; they require no additional secrets.

Versions use the form `0.0.N`. The source commit's absolute first-parent height plus the fixed migration offset `-281` determines `N`, making the first automatically published `main` commit version `0.0.9`; every later first-parent commit increments `N`. The package repository metadata records the complete source commit.

The feed is served at `https://nuget.stanoddly.dev/index.json` from the Cloudflare R2 bucket `nuget` through its custom domain, with Cloudflare's cache in front. The bucket can hold other packages in the same feed.

## R2 configuration

The bucket has these settings:

- name: `nuget`
- S3 endpoint: `https://<account ID>.r2.cloudflarestorage.com`
- default storage class: Standard
- custom domain: `nuget.stanoddly.dev`
- public development URL (`r2.dev`): disabled

Create an Account API token for R2 with **Object Read & Write** on the `nuget` bucket only. Add these GitHub Actions repository secrets:

- `R2_ACCOUNT_ID`: the Cloudflare account ID
- `R2_ACCESS_KEY_ID`: the token's access key ID
- `R2_SECRET_ACCESS_KEY`: the token's secret access key

The workflow installs Sleet 7.2.0 and creates its configuration in the runner's temporary directory. The token needs read, list, write, and delete access because Sleet reads feed state and creates and removes a root `.feedlock` while publishing. Do not add a bucket lock rule that covers `.feedlock` because it would prevent Sleet from removing this lock. R2 does not support the AWS SDK's streaming uploads, so the configuration sets `disablePayloadSigning` and the workflow sets `AWS_REQUEST_CHECKSUM_CALCULATION` and `AWS_RESPONSE_CHECKSUM_VALIDATION` to `when_required`.

Sleet uploads packages and other versioned files with `Cache-Control: public, max-age=31536000, immutable` and feed JSON with `Cache-Control: public, max-age=60`. Promotion files use the immutable value. The `stanoddly.dev` zone has these rules for `nuget.stanoddly.dev`:

- a Cache Rule that makes requests eligible for cache, with Edge TTL set to use the `Cache-Control` header if present and bypass the cache if not, and Browser TTL set to respect the origin
- Smart Tiered Cache
- a URL Rewrite Rule that serves `/index.json` for `/`

Missing files return 404 without `Cache-Control`, so a 404 is never cached. A new version's files are readable as soon as they are uploaded, and the version list shows it within a minute.

To rotate the credential, create a replacement with the same restrictions, update `R2_ACCESS_KEY_ID` and `R2_SECRET_ACCESS_KEY`, wait for or rerun a successful development publication, and then delete the previous token.

## nuget.org configuration

The nuget.org trusted-publishing policy uses these GitHub details:

- repository owner: `stanoddly`
- repository: `Pixely`
- workflow file: `publish-nuget.yml`

No NuGet API key is stored in GitHub. If nuget.org marks a new policy as pending full activation, publish a package within its displayed seven-day activation window. The window can be restarted from the policy page if it expires.

## Publishing to nuget.org

Open **Actions**, select **Publish to nuget.org**, and enter an exact version already present in the development feed. The version must advance beyond the highest numbered Pixely version on nuget.org unless the run is recovering that same version.

The workflow downloads the `.nupkg`, `.snupkg`, and manifest from the development feed, verifies their SHA-256 hashes, validates the version-to-commit mapping, requires a successful **Publish to development feed** run for the source commit, and publishes the existing files through nuget.org trusted publishing. It does not build or pack. After publication, it tags the recorded source commit and creates the GitHub release. Not every development version needs to be published, so the nuget.org sequence can contain gaps.

## Recovery

Development-feed publication is idempotent. Rerunning the same `main` workflow uses the same version, Sleet skips its existing package, and the workflow reuses the first stored package and symbol archives as the canonical publication artifacts. It verifies their version and source commit before deriving the immutable checksum manifest. Do not rerun a run from before the move to R2: a rerun uses its original workflow file, which publishes to Backblaze B2.

Publishing to nuget.org is also rerunnable. Duplicate uploads are skipped, an existing tag must point to the recorded source commit, and an existing GitHub release is retained. A checksum, version, commit, or tag mismatch stops the workflow instead of modifying published state.
