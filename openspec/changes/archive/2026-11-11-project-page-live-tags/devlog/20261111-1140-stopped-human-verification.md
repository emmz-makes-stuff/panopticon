# Stopped: 3.2 needs Emmz's two-browser check

<!-- Section: 3 · Tasks: 3.1 done (uncommitted), 3.2 open · Base: 32849b3 -->

## Where it stands

- **3.1** is done but not committed. `docs/DISCOVERY.md`'s Tags section now says the Project page's Tags list is
  live, for tags only, with counts as of the last list change.
- **3.2** is a human check, so this is a stop: live updates have to be seen in a real browser. §3 stays uncommitted
  until Emmz gives a PASS.

## The question

Does the Project page's Tags list follow another member's edits in a real browser? The recipe below is the one sent
to Emmz.

**Setup** (from the repo root):

```sh
docker compose up -d --wait
npm --prefix web ci && npm --prefix web run build
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Panopticon.Server --no-launch-profile
```

1. **Browser 1** (a normal window): sign in as `dev@localhost.test`. The link is in the server log, after
   `Development: not sending email to`. Create Project `PAN` (or open it), and invite `sam@localhost.test` as an
   editor.
2. **Browser 2** (a private window): sign in as `sam@localhost.test`, and open `PAN`'s page.
3. **Checks**, with both browsers on `PAN`'s page:
   - **a.** In browser 1, add `bug` and then `area: onboarding`. Browser 2 shows both, in sorted order, without a
     reload.
   - **b.** In browser 2, type something into the add-tag input without submitting. In browser 1, rename `bug` to
     `defect`. Browser 2 shows `PAN/defect`, and its typed text is still there.
   - **c.** In browser 2, choose Rename on `PAN/area: onboarding` and type a few letters. In browser 1, add `zone`.
     Browser 2's rename form stays open with what was typed.
   - **d.** With browser 2's rename still open, delete `PAN/area: onboarding` in browser 1. Browser 2's form closes,
     the row goes, and the status line reads "PAN/area: onboarding was deleted."
   - **e. Optional:** with a change reported into `PAN`, add a new tag from that change's tag picker in browser 1.
     Browser 2 shows it with "1 change".

## Next

Wait for Emmz's answer to 3.2.
- **On PASS:** tick 3.2, write the §3-done note recording it, and commit §3 as one commit. Then do the whole-change
  review, offer `code-review`, and then offer `/opsx:archive`.
- **On FAIL:** record what was seen, then fix it within §3, or as a review-fix section if it's bigger.
