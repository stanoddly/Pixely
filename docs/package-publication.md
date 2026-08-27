# Package publication

Pixely is packed once for every validated `main` commit. The resulting package is published immediately to the public development feed and can later be promoted unchanged to nuget.org.

## Development feed

The NuGet service index is:

```text
https://pixely.pages.dev/index.json
```

Cloudflare Pages hosts only the stable service index. Its resource URLs point to the public `pixely` Backblaze B2 bucket, where Sleet stores package metadata and package files. Adding packages or versions does not require another Pages deployment.

Development versions continue the nuget.org `0.0.N-alpha` sequence. Commit `d5e9babf3149d0cbdc75d74b5cea33bc5461739e`, the last `main` commit before automatic development publication, and nuget.org version number `8` are the fixed baselines; every later first-parent commit on `main` increments `N`. The package repository metadata records the complete source commit.

The **Publish Pixely development package** workflow runs after every push to `main`. It performs the full build and test suite, validates the package through isolated consumers on all supported hosts, publishes the `.nupkg` through Sleet, and stores the matching `.snupkg` and checksum manifest under `promotion/pixely/<version>/` in B2. Publication attempts are serialized, reruns reuse the first stored files, and existing content is never overwritten.

## One-time development-feed setup

The B2 bucket has these settings:

- name: `pixely`
- S3 endpoint: `https://s3.eu-central-003.backblazeb2.com`
- public file access
- lifecycle: keep only the last version of each file

Create a bucket-restricted application key with Read and Write access, no filename prefix, and **Allow List All Bucket Names** enabled. Add its values as these GitHub Actions repository secrets:

- `B2_KEY_ID`: application key ID
- `B2_APPLICATION_KEY`: application key secret

The tracked `packaging/sleet.json` contains only environment-variable placeholders. The key needs read, list, write, and delete access because Sleet reads feed state and creates and removes a root `.feedlock` while publishing. If Object Lock is enabled on the bucket, do not apply default retention to the feed files because it would prevent Sleet from removing this lock.

To rotate the credential, create a replacement with the same restrictions, update both GitHub secrets, rerun or wait for a successful development publication, and then delete the previous key.

## Consuming development packages

Add the development feed alongside nuget.org and map only `Pixely` to it:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="pixely-development" value="https://pixely.pages.dev/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
    <packageSource key="pixely-development">
      <package pattern="Pixely" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

Reference an exact development version:

```xml
<PackageReference Include="Pixely" Version="0.0.N-alpha" />
```

Do not use a floating Pixely version across the development and nuget.org feeds. A clean unauthenticated acceptance check can be performed with a new package directory:

```bash
packages_directory="$(mktemp -d)"
dotnet restore --configfile NuGet.config --packages "$packages_directory"
dotnet build --no-restore
```

## nuget.org setup

Create a nuget.org trusted-publishing policy with these GitHub details:

- repository owner: `stanoddly`
- repository: `Pixely`
- workflow file: `publish.yml`

No NuGet API key is stored in GitHub. If nuget.org marks a new policy as pending full activation, promote a package within its displayed seven-day activation window. The window can be restarted from the policy page if it expires.

## Promoting a prerelease

Open **Actions**, select **Promote Pixely prerelease**, and enter an exact version already present in the development feed. The version must advance beyond the highest nuget.org prerelease unless the run is recovering the same version.

The workflow downloads the `.nupkg`, `.snupkg`, and promotion manifest from B2, verifies their SHA-256 hashes, validates the version-to-commit mapping, and publishes the existing files through nuget.org trusted publishing. It does not build or pack. After publication, it tags the recorded source commit and creates the GitHub prerelease.

Not every development version needs promotion, so the nuget.org sequence can contain gaps.

## Recovery

Development publication is idempotent. Rerunning the same `main` workflow uses the same version, Sleet skips its existing package, and the workflow reuses the first stored package and symbol archives as the canonical promotion artifacts. It verifies their version and source commit before deriving the immutable checksum manifest.

Promotion is also rerunnable. Duplicate nuget.org uploads are skipped, an existing tag must point to the recorded source commit, and an existing GitHub prerelease is retained. A checksum, version, commit, or tag mismatch stops the workflow instead of modifying published state.
