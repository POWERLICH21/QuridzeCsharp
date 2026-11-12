# Kumo Threads

A demo online clothing shop: ASP.NET Core 8 minimal API + EF Core (SQLite) backend, and a plain HTML/CSS/JavaScript storefront served from `wwwroot`.

## Run

```bash
cd KumoThreads
dotnet run
```

Then open http://localhost:5080. The SQLite database `shop.db` is created and filled with sample products on first start.

## Features

- Catalogue with categories, collections, search, sorting, sale/new/trending filters and paging
- Product page with sizes, quantity, reviews and related products
- Cart drawer, cart page, wishlist (stored in the browser)
- Checkout that re-checks prices and stock on the server, order confirmation, order tracking by number + email
- Accounts (register, sign in, order history) using cookie authentication and hashed passwords
- Newsletter sign-up and contact form
- Admin panel (`#/admin`): sales stats, order status updates, product add/edit/delete, contact messages

## Admin account

The admin user is created at start-up from the `Admin:Email` and `Admin:Password` settings.
For local development these are in `appsettings.Development.json`
(`admin@kumothreads.local` / `ChangeMe!123`). Change them before using the app anywhere else,
for example with the environment variables `Admin__Email` and `Admin__Password`.

## Notes

- No payment provider is connected: checkout records the order only.
- Product pictures are SVG drawings generated in the browser from each product's colours and motif.
