# Contributing

Thanks for considering it. This is a small plugin with a narrow job, so the bar is mostly
about not breaking the two guarantees it makes: your tags and your collections stay yours.

## Before you start

Open an issue for anything larger than a bug fix. A new interest provider, a change to how
ownership is tracked, or a new filtering rule is worth agreeing on first.

## Getting set up

See [DEVELOPMENT.md](DEVELOPMENT.md). In short: .NET 9 SDK, `dotnet test`, done.

## The rules that matter

**A provider that cannot answer must never look like a provider that answered "nothing".**
`ProviderResult.Failure` and `ProviderResult.Empty` mean different things; conflating them
would make an outage strip metadata from a whole library. Any new provider needs a test
covering its down state.

**Never modify a collection the plugin did not create.** Ownership requires both the provider
id stamp inside Jellyfin and a record in the plugin's state. Both, not either.

**Never remove a tag the plugin did not write.** `ProcessedItemStore` is the source of truth
for what is ours.

**Never let an exception reach a library scan.** Background handlers and the pipeline log and
continue.

**Never log an API key.** Provider failure messages are written by hand for this reason;
don't interpolate request URLs into them.

## Code style

The build runs StyleCop and the full .NET analyzer set with warnings as errors, which settles
most style questions before review. Beyond that:

- One public type per file.
- XML documentation on public members — it is enforced, and it is what the analyzers read.
- Comments explain *why*, not *what*. If a line needs a comment to say what it does, rename
  something instead.

## Commits and pull requests

Write commit messages that explain the reasoning, not just the change. Keep unrelated changes
in separate commits. Make sure `dotnet build --configuration Release` and `dotnet test` both
pass — CI runs exactly those.

## Reporting bugs

Include your Jellyfin version, the plugin version, which provider you use, and the relevant
log lines with the log level set to Debug. If a title is tagged wrongly, its IMDb id is worth
more than its name.
