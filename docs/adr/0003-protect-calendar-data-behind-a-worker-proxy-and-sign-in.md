# 3. Protect calendar data behind a Worker proxy and sign-in

- **Status:** Accepted
- **Date:** 2026-11-12

## Context

The original plan had the ptv-display frontend call `GET /api/commute/next` directly with an `X-Api-Key` header. ptv-display is a public website, so any key shipped to the browser is readable by anyone. The response also reveals where I am going and when, which is personal information.

ptv-display is a public board that anyone can open. Two things have to be protected:

1. The CommuteApi key and endpoint.
2. The commute result itself (place and event time), which must only reach me.

## Decision

1. **Server-to-server only.** The ptv-display Worker gets a new `/api/commute` route. The Worker calls CommuteApi with the key held as a Worker secret (`COMMUTE_API_KEY`). The browser never sees the key or the CommuteApi URL, so CommuteApi does not need to allow any browser origin through CORS.
2. **Sign-in in front of the commute route.** `/api/commute` on the Worker sits behind Cloudflare Access, with Microsoft and Google as identity providers and a policy that allows only my email address. The Worker also checks the `Cf-Access-Jwt-Assertion` header, so a misconfigured Access policy fails closed. Signed-out visitors still get the normal public board without a commute card.
3. **Admin pages need Microsoft sign-in.** The CommuteApi `/Places` and `/Admin` pages use Microsoft Entra ID sign-in (`Microsoft.Identity.Web`) and allow only my account.
4. **Minimal response.** The commute response contains the saved place name, stop, route and times. It never contains the event title, location text or attendees.

## Alternatives considered

- **Browser calls CommuteApi with a key.** Rejected: the key is public.
- **Multi-user: each viewer signs in and sees their own calendar.** Deferred. It needs per-user places, per-user token storage and consent handling, which is a different product. Kept on the roadmap.

## Consequences

- The wall display has to sign in to Cloudflare Access once, and again when the session expires. The session length is set in the Access application.
- Signing in with Google only proves who is viewing. Reading a Google calendar is still the separate "Google Calendar provider" roadmap item.
- CommuteApi stays a single-user service.
