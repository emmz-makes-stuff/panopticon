# Review fix sections §7–§10

<!-- After the change-done note · Head: 93f3d13 · Planning, agreed by Emmz -->

`/code-review high` on the branch gave 10 candidates, none reproduced. I checked each against the code:
- **Real:**
  - #1 the in-flight report racing the watcher (narrow, within one bridge);
  - #2 a `Tasks.md` case mismatch throwing `InvalidOperationException`;
  - #3 `ChangeCards.GetAsync` doesn't filter out archived rows, so a late `change-updated` can put the card back;
  - #8 a non-UTC `before` gives a 500. The review's second half, about runs of equal times, is wrong: `HistoryAsync`
    already ends a page before such a run;
  - #6 S3 performance; #7 popstate rebuilds the page; #9 no retry on the history page; #10 dead code.
- **As specced:** #4. The spec accepted "the agent's next report" as the only retry.
- **Spec gap:** #5, no limit on how many documents a report carries.

Emmz chose all four groups, and "yes" to saying "archived as `<folder>`" in the reply.
- **Values I used** from the option text: at most 500 documents, and a retry every 30 seconds.
- **Specs amended:**
  - progress-reporting, Document checks: 500 in number, and a "Too many documents" scenario.
  - cli-bridge, Document size limits: the count, and a "Too many files" scenario.
  - cli-bridge, Archived change reported: the reply wording.
  - cli-bridge, Watching for archives: the 30 s retry, and a "Server unreachable at archive time" scenario.
  - cli-bridge: a new requirement, "One report at a time per change", split out because the watcher requirement
    went over 500 characters.
- `VALIDATE_EXIT:0`.

Sections:
- §7 correctness: #1, #2, #3, #8, #10.
- §8 bridge archive reports: #4 and the reply wording.
- §9 document count (#5) and storage (#6).
- §10 web navigation: #7, #9.

## Next

- Emmz agreed §7–§10 as written. Build §7 from 7.1.
