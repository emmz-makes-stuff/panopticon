# Review fixes agreed: §8 and §9

<!-- After the change-done note · Base: 5b39e77 · `/code-review high` on main...change/visit-requests -->

`/code-review` reported 10 findings, none verified by it. With Emmz:

- **§8, visit safety (agreed: all):** show the normalised `href` as the link text (IDN look-alikes); always strip a
  `panopticon_answer` the agent supplied; tighten the `request_visit` matcher; "couldn't visit" for Can't; D7
  notification wording updated to match the code.
- **8.2 extends to auto-allow (Emmz: yes).** With the old code, a visit that isn't held gets a terminal permission
  prompt. `updatedInput` only applies alongside `permissionDecision: allow`, so the hook now always allows
  `request_visit`. That's fine: it only returns text.
- **§9, shared code (agreed: all):** the kind-to-waiting-state map in Contracts, one allow-with-injected-field helper,
  one JSON text helper, and the dashboard reading `kind`.
- **Dropped:** the claim that the migration's tightened constraint breaks on old permission rows with a NULL outcome.
  On `main`, `PromptAnswerEndpoints.cs:129` always sets an outcome for a permission answer, so no such row can exist.

## Next

- §8, task 8.1 (`web/src/promptForm.ts`, the visit link's text). 8.3 also touches `../panopticon-skills`: commit it
  there on `change/visit-requests`, unpushed.
