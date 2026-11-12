# BauBico

A pre-order online clothing shop: ASP.NET Core 8 minimal API + EF Core (SQLite) backend, and a plain HTML/CSS/JavaScript storefront served from `wwwroot`.

## Run

```bash
cd BauBico
dotnet run
```

Then open http://localhost:5080. The SQLite database `shop.db` is created and filled with sample products on first start.

## Features

- Catalogue with categories, collections, search, sorting, sale/new/trending filters and paging
- Product page with sizes, quantity, reviews and related products
- Cart drawer, cart page, wishlist (stored in the browser)
- Pre-orders instead of instant purchase: no payment is taken, prices and remaining pre-order slots are re-checked on the server, and customers can track a pre-order by number + email
- Accounts (register, sign in, order history) using cookie authentication and hashed passwords
- Newsletter sign-up and contact form
- Admin panel (`#/admin`): pre-order stats and status updates, product add/edit/delete with prices and photo upload, contact messages

## Admin account

The admin user is created at start-up from the `Admin:Email` and `Admin:Password` settings.
For local development these are in `appsettings.Development.json`
(`admin@baubico.local` / `ChangeMe!123`). Change them before using the app anywhere else,
for example with the environment variables `Admin__Email` and `Admin__Password`.

## Notes

- No payment provider is connected: a pre-order is recorded and the shop owner arranges payment by email.
- The estimated shipping time shown to customers is `Shop:PreOrderShipsIn` in `appsettings.json`.
- Uploaded product photos are stored in `wwwroot/uploads`. A product without a photo shows an SVG drawing generated from its colours and motif.
