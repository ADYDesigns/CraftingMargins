# Reporting a problem, and contributing

Crafting Margins is free software, shared as it is. **There is no support**: nobody is paid to answer questions or fix things, and a report may sit unanswered. Reports are still welcome, because they are how problems get found.

## Reporting a problem

Open an issue on the project's [Issues](../../issues) page and include:

1. **The version and schema** from the About tab (for example "Version 0.9.0 · Schema 1").
2. **Windows or macOS**, and which version. On a Mac, also whether it's Apple silicon or Intel.
3. **What you did, what you expected, and what happened instead.** The exact wording of any message helps.
4. **The log**, if there was an error. Logs are in the `Logs` folder inside your data folder (the About tab shows where that is). They hold no buyer details and no money amounts.

### Never post your Etsy files

Etsy's order exports contain your buyers' names and addresses, and your statements contain your shop's finances. **Don't attach them to an issue.** If a file won't import:

- Say which kind of file it is (statement, sold orders, sold order items, listings) and paste **only its first line** (the column headings).
- If one row seems to be the cause, retype that row with made-up values that keep its shape (`"October 5, 2026",Fee,"Listing fee","Listing #123",USD,--,-$0.20,-$0.20,--`).

The same goes for the cost files from **Export costs**: they hold what your products cost you. Describe the problem, or retype a row with made-up values.

### Shops outside the United States

The app was designed for US shops paid in US dollars. VAT, other currencies and the fees Etsy charges elsewhere weren't taken into account. Reports about those are useful to know about, but they describe missing features, not bugs.

## Contributing code

- **Build and test:** `dotnet build` and `dotnet test` (needs the .NET 10 SDK). All tests must pass. The UI tests open the real screens on the demo shop without a display. GitHub runs every test on Windows and macOS for each push and pull request.
- **Where things are:** `CraftingMargins.Core` is the arithmetic, with no screens and no files; `CraftingMargins.Data` is storage, imports and the Etsy client; `CraftingMargins.App` is the screens, one file per tab in `Views`; `packaging/macos` builds the Mac app.
- **Money is `decimal`**, and every fee is rounded through `Money.RoundFee`.
- **Never guess.** A statement line or a sale the app can't place is shown under Needs attention. It is never given a default.
- **Never store buyer details.** Names, emails, addresses and buyer-typed text are dropped while a file is read. Tests check this; keep them passing.
- **Use made-up data only** in tests and examples. Never commit a real Etsy export, a real database or a real cost export.
- **The database layout (schema)** is stamped with `Database.SchemaVersion` and shown on the About tab. Once a version has been released, people have data in that layout: a change to a table or column must bring their data up to the new layout without losing anything, with a test that upgrades a database from each released schema. The schema number goes up once, in the next release, however many changes it has; a release that changes no table keeps the same number.
- **Dependencies** must be free for commercial use and compatible with the GPL v3. Add each new one to `NOTICE.md`.
- **Plain words on screen.** The people using this are makers, not accountants or programmers.

By contributing you agree your contribution is licensed under the GNU GPL v3 or later, like the rest of the project.

## If you publish your own version

- See `NOTICE.md`: give it a different name and icon, keep the copyright notices, and don't present it as coming from ADY Designs, LLC.
- **Etsy's API Terms apply to you.** Whoever runs an app that uses the Etsy API is responsible for the data it handles, including telling Etsy within 24 hours of any breach. Register your own Etsy app; this app stores no buyer names, emails or addresses, and yours should keep it that way.
