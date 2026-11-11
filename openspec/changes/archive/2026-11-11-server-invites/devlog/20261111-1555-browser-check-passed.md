# Browser check passed

<!-- Section: 2 · Tasks: 2.2 -->

Emmz ran the 2.2 recipe against a local Development server: "pass on all of that, kit got logged out and
everything". So:

- inviting showed the status line and listed Kit with Remove;
- the logged invitation email linked to the sign-in page;
- re-inviting showed "already a member";
- Kit signed in and saw only themselves;
- removing Kit ended their session.

Her first `dotnet run` failed with `CS2012 Cannot open '…/Panopticon.Contracts/obj/Debug/net10.0/Panopticon.Contracts.dll'
for writing -- Access to the path … is denied`. This was a machine problem, not a code one. A long-lived compiler
server, probably started by an earlier sandboxed build, kept the sandbox's write limits. `dotnet build-server shutdown`
fixed it, and the Debug build then succeeded with `0 Error(s)`. I offered to add this symptom to her global CLAUDE.md
sandbox notes, and she didn't take that up.

## Next

- Run `make gates` for §2, write the section-done note, commit §2.
- Then the whole-change review (`git diff 2c569ee..HEAD`), the change-done note, offer `/code-review`, offer
  `/opsx:archive`.
