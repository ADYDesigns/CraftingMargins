# Changelog

What changed in each version, newest first. Versions before the first public release were early test builds.

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
