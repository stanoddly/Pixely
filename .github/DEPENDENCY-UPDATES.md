# Dependency updates

The **Renovate** workflow runs self-hosted Renovate daily and on demand. It reads `.github/renovate.json5` and opens one grouped pull request for NuGet package updates, including the `SlangDxcBundle.Toolchain` project SDK version in `src/Pixely.SdlangCompiler/build/Pixely.SdlangCompiler.props`.

## Token

Renovate needs its own token because pull requests opened with the repository-scoped `GITHUB_TOKEN` do not trigger the pull request workflow, so they never receive the required validation check. Add the token as this GitHub Actions repository secret:

- `RENOVATE_TOKEN`: a classic personal access token of `botoddly` with the `repo` scope

`botoddly` is a collaborator rather than the repository owner, and fine-grained tokens cannot access repositories where the user is only a collaborator. The `repo` scope grants access to every repository `botoddly` can access, not only Pixely.

Renovate commits as `Renovate <renovate@pixely.invalid>`, set by `gitAuthor` in `.github/renovate.json5`. It treats commits by that email as its own and may overwrite them when it rebases, so a fix pushed to a Renovate branch must use another email.

To rotate the token, create a replacement with the same scope, update the secret, run the **Renovate** workflow manually and check that it succeeds, and then delete the previous token. An expired or missing token fails the daily run, and no update pull requests appear until it is replaced.
