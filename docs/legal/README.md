# Terms and conditions, and disclaimer

> **Draft pending legal review.** The text was written from LinkFast S.A.'s points and must be reviewed by a lawyer
> before a stable release.

The terms and the disclaimer live in one file, [`src/Arcanist.Core/Legal/terms.json`](../../src/Arcanist.Core/Legal/terms.json),
in Spanish and English, embedded in the server. Change the text there and **change its `version`**: everyone is asked
to accept the new version.

- **Who sees what.** The setup wizard shows the administrator's version (`audience` ADMIN) and does not complete
  until it is accepted (`SetupRequest.acceptedTerms` with the current version; kept as the `legal.acceptance`
  setting, with who and when). Players show the users' version (`audience` USER: without the section on trademarks
  and commercial services) the first time a user enters; Webamp for Arcanist keeps the acceptance in the browser.
- **Mode.** A server is `SELF_HOSTED` (the software as is, bugs only as GitHub issues), or `LINKFAST_SERVICE` while an
  extension of LinkFast S.A.'s service runs (`LegalService.ServiceExtensions`: today `arcanist-cloud-storage`, its
  managed storage), whose support and warranties are those of the service's contract. Sections say the modes and
  audiences they apply to.
- **API.** `legalTerms(language, audience)` / `GET /rest/v1/legal/terms?language=&audience=`, without
  authentication (also before setup). `{issuesUrl}` in a paragraph becomes the issues of the repository the server's
  source comes from (`serverInfo.sourceUrl`).

## About commercial services (SaaS)

Arcanist is AGPL-3.0-or-later. The license lets anyone run it as a service, also commercially, as long as they
publish their source; terms cannot take that right away (the AGPL forbids further restrictions), and code
contributed under the DCO cannot be relicensed. What LinkFast S.A. can reserve is its **trademarks**: the terms
reserve offering Arcanist as a commercial service under the names "Arcanist" and "LinkFast", their logo and look, and
say that the license over the code is not limited. Registering the trademark is what makes that enforceable.
