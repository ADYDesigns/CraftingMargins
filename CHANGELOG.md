# Changelog

What changed in each version, newest first. Versions before the first public release were early test builds.

## 0.9.4 (November 27, 2026)

### Fill in your costs with Excel
- **Cost workbook** on Products & Costs saves one Excel file to fill in: a page of instructions, then Materials, Parts and Cost rules sheets with drop-down lists for units, materials and parts.
- The Cost rules sheet already lists every listing, with its options spelled exactly as Etsy names them, and the option values that only past orders used. Buyers' personalization is never included.
- Drop the saved workbook on Home to bring it in. It goes through the same checks as the cost CSV files, and a mistake is reported with its sheet and row. Starter rows left empty are skipped.

### Easier to read tables
- **Pricing shows each result once.** Combinations of a listing that earn exactly the same, such as three colors at one price and cost, are one row: "Number Of Tools: 2 Tools; Primary color: White, Silver or Black". Anything priced or costed differently keeps its own row, and every combination is still checked. The planned sale and the Excel export are grouped the same way.
- **Columns can be resized.** Drag the edge of a column heading in any table to widen it.
- **Parts needing attention.** A checkbox on the Parts tab shows only the parts that are missing something: nothing in them yet, a material with no price, or no machine time when you track it. The dot beside each part turns red for any of these, and hovering over it says what's missing.
- **Make a part from the pricing check.** Right-click an option that has no cost rule and choose **Create a part for this option**. It makes an empty part named after the listing and option, plus the cost rule linking that option to it (options a row combines, like colors, are left out of the rule), then opens the part on the Parts tab to fill in. Options that already have a cost show the menu item greyed out. **Open this listing's cost rules** is on the same menu.
- A part picked from elsewhere, like one just made from the pricing check, is scrolled into view on the Parts tab.
- **The window opens the way you left it:** maximized, or at its last size and place. A place that's no longer on any screen (a monitor unplugged) is ignored and the window is centred.
- An option whose parts add up to $0 (a part not filled in yet) shows on Pricing as having no cost yet, instead of a green dot with too high a profit.
- **Sort by the dot.** Click the empty heading above the green and red dots (Parts, Pricing, a listing's costs) to bring the red ones to the top.

### Fixes
- **Export costs no longer rounds.** Prices and amounts were written with two decimals, so a price of 0.015 per gram came back as 0.02 when the files were imported again. They are now written in full.
- **Shipping insurance** (Etsy's "Shipsurance Package Insurance" line) counts as shipping, on the same order as its label. Before, it was an unknown line and the month failed its self-test.
- **Card fees for orders that haven't shipped:** the Etsy sync now asks for every order's card fee, since Etsy charges it at the sale. Until Etsy has one, an unshipped order is no longer flagged for a missing processing fee; profit always uses the fee on the statement.

## 0.9.1 (November 27, 2026)

Fixes for connecting to Etsy. If you use the Etsy API, please update.

- **Sync now finishes.** Etsy sends one field in its payment ledger as a number where its documentation says text, which stopped every sync partway. The app now accepts either.
- **A sync that stops partway still shows what it saved.** Orders and listings pulled before a problem appear right away, without restarting the app.
- **Setting up your Etsy app:** Etsy asks for the callback URL only after the app is created. The steps in the app and the README now say to edit the app and add it once the keys appear.
- After a sync, the app says when the Monthly P&L still needs your monthly statement CSVs, and the Getting started checklist explains that Etsy's API doesn't provide statements.
## 0.9.0 (November 24, 2026)

The first public release.

### For any maker, not only 3D printing
- Materials have their own unit: grams, ounces, yards, milliliters, or "each" for things bought by the piece.
- A part is a recipe of several materials (a candle: wax, fragrance, a wick, a jar). Hardware is now ordinary materials priced each, with a name and a price history.
- Two kinds of time, both optional: machine time (a printer, laser or kiln) and your own hands-on time. Your own time can be given an hourly rate; it never counts in the tax summary.
- Pick what you make on the first-run screen. It sets starting points only: units, the names of your first expense categories, the examples in the hints.
- Screens say "materials", "components", "machine time" and "waste allowance" in place of printing terms.

### Numbers
- A shipping label bought up to a week before the package ships is matched to its order. Label adjustments follow their label. Label money that can't be matched is shown, not dropped.
- An order whose Offsite Ads fee was later credited back still counts as an ad sale.
- The tax summary says which costs it leaves out when supplies are counted as bought.
- Statement files can no longer silently shrink a month that is already stored.
- Amounts like `$-1.04` are read as negative; `1,04` is refused instead of being read as 104.

### Listings and costs
- Listings are stored by Etsy's listing number. A listings file no longer wipes prices from the API.
- Listings that haven't sold yet can be given cost rules.
- A listing can be marked as really costing $0 to make.
- Export your materials, parts and cost rules as CSV files, and bring them back by dropping them on Home.

### Safer
- Unexpected errors show a message and are logged; they no longer close the app.
- Unsaved edits are kept when something else reloads, marked next to each Save button, and asked about before closing.
- Backups are made on close only when something changed (keep 0 to 10, default 3) and include category rules and settings.
- Only one copy of the app can be open on a data folder.
- One Etsy shop per data folder.
- "Delete all data" empties the database and tells you where your backups are.

### Using it
- Import, refresh and sync run in the background.
- Date pickers; chart tooltips; a setting for the keyboard focus outline in tables; names for screen readers.
- Numbers follow the computer's regional format; month names stay in English.
- A first sync that is cut off carries on next time.

### Project
- Schema 1 is the database layout the first release ships with.
- The main window is split into one file per tab.
- A Mac app for Apple silicon and Intel Macs, with its own icon. It keeps the Etsy secrets in the login keychain. Every automated test passes on macOS.
- Every push is built and tested on Windows and macOS by GitHub.

## 0.7.0 and earlier

Test builds: CSV import, Monthly P&L with self-test, cost rules, the Etsy API sync, reports, pricing tools, expenses, the tax summary and the demo shop.
