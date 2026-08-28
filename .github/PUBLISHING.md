# Package publishing

Pixely is packed once for every validated `main` commit. The resulting package is published immediately to the development feed and can later be published unchanged to nuget.org.

## Development feed

Pull requests run the complete build, test, integration, NativeAOT, and package-consumer slice on Linux ARM64. After merge, the **Publish to development feed** workflow runs the full validation on Linux x64, Windows x64, macOS x64, and macOS ARM64, then validates the canonical package through isolated consumers on those four platforms. It publishes the `.nupkg` through Sleet and stores the matching `.snupkg` and checksum manifest under `promotion/pixely/<version>/` in B2. Publication attempts are serialized, reruns reuse the first stored files, and existing content is never overwritten.

When a development publication attempt completes, the **Report development publication** workflow posts its result and a link to the run on the merged pull request. Successful publication comments include the version and a direct package download link. Each rerun is reported separately. The reporting workflow uses the temporary repository-scoped `GITHUB_TOKEN`; it requires no additional secrets.

Versions use the form `0.0.N`. The source commit's absolute first-parent height plus the fixed migration offset `-281` determines `N`, making the first automatically published `main` commit version `0.0.9`; every later first-parent commit increments `N`. The package repository metadata records the complete source commit.

Cloudflare Pages hosts the service index at `https://pixely.pages.dev/index.json`. Its resource URLs point to the public `pixely` Backblaze B2 bucket, so adding packages or versions does not require another Pages deployment.

## B2 configuration

The bucket has these settings:

- name: `pixely`
- S3 endpoint: `https://s3.eu-central-003.backblazeb2.com`
- public file access
- lifecycle: keep only the last version of each file

Create a bucket-restricted application key with Read and Write access, no filename prefix, and **Allow List All Bucket Names** enabled. Add its values as these GitHub Actions repository secrets:

- `B2_KEY_ID`: application key ID
- `B2_APPLICATION_KEY`: application key secret

The workflow installs Sleet 7.2.0 and creates its configuration in the runner's temporary directory. The key needs read, list, write, and delete access because Sleet reads feed state and creates and removes a root `.feedlock` while publishing. If Object Lock is enabled on the bucket, do not apply default retention to feed files because it would prevent Sleet from removing this lock.

To rotate the credential, create a replacement with the same restrictions, update both GitHub secrets, wait for or rerun a successful development publication, and then delete the previous key.

## nuget.org configuration

The nuget.org trusted-publishing policy uses these GitHub details:

- repository owner: `stanoddly`
- repository: `Pixely`
- workflow file: `publish-nuget.yml`

No NuGet API key is stored in GitHub. If nuget.org marks a new policy as pending full activation, publish a package within its displayed seven-day activation window. The window can be restarted from the policy page if it expires.

## Publishing to nuget.org

Open **Actions**, select **Publish to nuget.org**, and enter an exact version already present in the development feed. The version must advance beyond the highest numbered Pixely version on nuget.org unless the run is recovering that same version.

The workflow downloads the `.nupkg`, `.snupkg`, and manifest from B2, verifies their SHA-256 hashes, validates the version-to-commit mapping, and publishes the existing files through nuget.org trusted publishing. It does not build or pack. After publication, it tags the recorded source commit and creates the GitHub release. Not every development version needs to be published, so the nuget.org sequence can contain gaps.

## Recovery

Development-feed publication is idempotent. Rerunning the same `main` workflow uses the same version, Sleet skips its existing package, and the workflow reuses the first stored package and symbol archives as the canonical publication artifacts. It verifies their version and source commit before deriving the immutable checksum manifest.

Publishing to nuget.org is also rerunnable. Duplicate uploads are skipped, an existing tag must point to the recorded source commit, and an existing GitHub release is retained. A checksum, version, commit, or tag mismatch stops the workflow instead of modifying published state.
