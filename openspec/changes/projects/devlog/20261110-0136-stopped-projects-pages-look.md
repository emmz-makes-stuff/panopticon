# Stopped: the Projects pages need Emmz's look-and-feel check (§7.1 built, uncommitted)

<!-- Section: 7 · Tasks: 7.1 built, not ticked · Base: d294f5a -->

§7.1 is built and its view tests are green, but it isn't ticked or committed. Per `CLAUDE.md`'s stop rules, the look
and feel of the dashboard and the `frontend-design` result need Emmz's human check first.

**Gates run so far** (not the full `make gates`; that runs once §7 is done):
- `projects.test.ts`: 28/28. The whole web suite: 272/272.
- `WEB_BUILD_EXIT:0`, `WEB_LINT_EXIT:0`, `WEB_FORMAT_EXIT:0`.

## What's built

- **The `frontend-design` pass** is recorded as a new "Projects" section of `web/DESIGN.md`, before "Pages: a change".
  - **The one bold thing is the code plate** (`.project-code`): the code in Gabarito 700, letter-spaced 0.08em, in a
    1px `--ink-muted` box with a 3px radius. The idea is a cell's number on its door.
  - **Three sizes:** 31px on the Project page (its loudest thing), 16px in a fixed column on the list, and 12.8px for
    cards and rows in §8.
  - **"Personal"** is a dashed `--rule` plate in the body face: the empty bar's "nothing here" vocabulary.
  - **Colour:** never teal and never spark.
- **The code:**
  - **`web/src/views/projects.ts`:** `projectsView` (`/projects`) and `projectView` (`/projects/:id`).
    - **Exports:** `codePlate(code | null)` and `linkFile(id)`, for §8.1 to reuse.
    - **Client checks:** the code rule (the same wording as `ProjectCodes.Rule`) and the 1–100 name rule are checked
      before sending. The code is sent in capitals.
  - **`dom.ts`:** a `selectField` helper.
  - **`shell.ts`:** an `Env.copy` / `ShellDeps.copy` clipboard seam (default `navigator.clipboard.writeText`). The
    test shell takes `copy` too.
  - **Routes:** `/projects` and `/projects/:id`.
  - **The header:** Projects is the first link. `shell.test.ts` and `history.test.ts` expect it.
  - **`styles.css`:** a "Projects" block after the member rows.
- **Choices I made that Emmz may want changed:**
  - **Owners' role changes** are a `select` in each row that applies at once, with no Save button and no
    confirmation. A refused change, such as the last owner, puts the select back and shows the server's message.
  - **Demoting yourself asks first** (Emmz's answer, 2026-11-10, after a side agent pointed out that one of two
    owners could demote themselves by mistake). The message is "Make yourself an editor of PAN? You'll lose owner
    controls, and only another owner can give them back." Cancel puts the select back and sends nothing. Others' role
    changes still don't ask. Tests: 30/30 in `projects.test.ts`, then `WEB_LINT_EXIT:0` and `WEB_FORMAT_EXIT:0`.
  - **Owners can change their own role,** and the server refuses it for the last owner. There's no Remove on your own
    row: that's Leave.
  - **A created Project opens its page.**
  - **A Project you aren't in** shows "There's no such Project" as the heading, with the back link.
- **Not done yet:** §7.2 (the Members page and `canRemoveMembers` in `SessionMember`/`MEMBER`) has not started.

## The question

Do the Projects pages look and work right: the list, the create form, the Project page with its code plate, the link
block and Copy, members and roles, invite, and Leave? Check light and dark, and desktop and phone width.

**Recipe** (from the repo root):

```sh
docker compose up -d --wait
npm --prefix web ci && npm --prefix web run build
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Panopticon.Server --no-launch-profile
```

1. Open <http://localhost:7480>, and sign in as `dev@localhost.test`. The link is in the server log's "Development:
   not sending email" warning.
2. Choose **Projects** in the header. Check the empty-list text.
3. Create `Panopticon` with code `pan`: it should open the page with a large `PAN` plate. Then go back and try `pan`
   again to see "The code PAN is taken. Choose another.", and try `P-1` to see the code rule.
4. On the page:
   - Choose **Copy**, and paste somewhere to check the `.panopticon.json`.
   - Rename the Project.
   - Invite `sam@example.com` as an editor, change Sam's role, and remove Sam.
   - Make yourself an editor, and choose **Leave this Project**: both should give "A Project needs an owner…".
5. Look at it in dark mode, and at phone width (about 375px) in the browser's device toolbar.

**On PASS:** tick 7.1, and carry on to 7.2 in the same section, so it's still one §7 commit.

**On FAIL:** apply Emmz's changes to `web/DESIGN.md` "Projects", the CSS and the view, re-run the web tests, and ask
again.

## Next

- Wait for Emmz's answer to the look-and-feel question above, then follow On PASS or On FAIL.
