# Contributing

Thanks for helping! Bug reports, ideas and pull requests are welcome.

## Never share real chats

Do **not** attach exports, screenshots, logs or databases from real chats to issues or pull requests — not
even "just one message". Reproduce problems with the synthetic demo archive instead:

```sh
dotnet run --project src/DemoArchive -- ./demo-archive
```

If a bug only shows with your data, describe the *shape* (field names, types, counts), not the content.

## Development

- .NET 10 SDK and Node.js 22+.
- `dotnet test` runs the backend tests; `npm test` in `src/web` the UI tests; `npx playwright test` the
  end-to-end tests against a demo archive.
- `dotnet run --project src/Explorer -- ./demo-archive` starts the explorer on the demo data.

## Pull requests

- One topic per pull request, with tests.
- Keep the privacy rules in the README intact: no network calls, no writes into the archive, no chat content
  in logs.
- By contributing you agree that your contribution is licensed under the MIT License.
