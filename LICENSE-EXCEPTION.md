# Arcanist Extension Exception

> **Draft, pending legal review.** Until it is reviewed, its wording may change; its intent will not.

Arcanist is licensed under the GNU Affero General Public License, version 3 or (at your option) any later version
(see [LICENSE](LICENSE)). As an additional permission under section 7 of that license, the copyright holders of
Arcanist grant the following.

## Additional permission

1. **Definitions.**
   - "The SDK" is the `Arcanist.Extensibility` library, as published in this repository, and the HTTP APIs the
     Arcanist server serves (GraphQL at `/graphql` and REST at `/rest/v1`), as documented in `docs/`.
   - "An Extension" is a work that the Arcanist server loads as an extension (a package with an
     `arcanist-extension.json` manifest) or as a web player (a package with an `arcanist-frontend.json` manifest),
     and that interacts with Arcanist **only through the SDK**: it references no other part of Arcanist (in
     particular, not the `Arcanist.Core` assembly), and contains no code copied from Arcanist other than the SDK's
     public interfaces and the examples in its documentation.

2. **Permission.** You may convey an Extension, and combine it with Arcanist by having the Arcanist server load it,
   under terms of your choice, including proprietary terms, without the terms of the GNU Affero General Public
   License applying to the Extension. The terms of that license continue to apply to Arcanist itself, the SDK
   included, and to any modified version of them.

3. **What is not covered.** This permission does not cover:
   - works that reference `Arcanist.Core` or any part of Arcanist other than the SDK, or that copy its code: they
     must be licensed in a way compatible with the GNU Affero General Public License;
   - modified versions of Arcanist or of the SDK.

4. **Trademarks.** This permission grants no right to use the names Arcanist, LinkFast or Ultimate, or their logos.
   See the terms of use shown by the setup wizard.

5. **Removal.** As section 7 allows, anyone who conveys a modified version of Arcanist may remove this additional
   permission from their version. It then no longer applies to that version.

## Contributions

Contributions to Arcanist are accepted under the GNU Affero General Public License, version 3 or later, together with
this additional permission (see [CONTRIBUTING.md](CONTRIBUTING.md)).
