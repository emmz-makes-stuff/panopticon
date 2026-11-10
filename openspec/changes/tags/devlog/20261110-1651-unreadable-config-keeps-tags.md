# Decision: an unreadable config file no longer blocks reports

<!-- Section: 6 (follow-up) · Base: 4548970 -->

- **The problem:** as §6 landed, a `config.json` that couldn't be read at all (not valid JSON, or no permission) made
  every bridge report fail. That held even when the token came from `PANOPTICON_TOKEN`, so one broken file stopped
  every card updating. A side agent flagged it. The spec only asks for an error on a malformed *tag*, so failing
  on an unreadable file had been my call, not the spec's.
- **Emmz's answer (2026-11-10):** "Send, keep tags".
  - When the file can't be read, `ReadPersonalTags` returns null, so the report goes without `personalTags`. The
    server then keeps the tags it has (the null-keeps rule from D5, built in §4).
  - The bridge logs a stderr warning naming the file, never its content.
  - A readable file with a bad tag, or more than 20 tags, still fails the report, as the spec's "A malformed file"
    asks.
- **Unchanged:** `panopticon tags` still refuses to edit an unreadable file, so nothing in it is lost. `login` still
  replaces it.
- **Tests:** the old `An_unreadable_config_file_is_a_tool_error_without_its_content` test is replaced by
  `An_unreadable_config_file_still_reports_and_keeps_the_tags_the_server_has`. It reports `dev`, breaks the file,
  and reports again. The report succeeds, the server still has `dev`, and the log names the file without the
  token.
- **Docs:** `docs/cli.md`'s personal-tags section and troubleshooting say what happens now.
- **Gates:** `BUILD_EXIT:0`, `AOT_EXIT:0`, `TEST_EXIT:0` (720 tests, 0 failed), `FORMAT_EXIT:0`,
  `WEB_INSTALL_EXIT:0`, `WEB_BUILD_EXIT:0`, `WEB_TEST_EXIT:0`, `WEB_FORMAT_EXIT:0`, `WEB_LINT_EXIT:0`,
  `VALIDATE_EXIT:0`, `GATES_EXIT:0`.

## Next

- Still stopped on the §7.1 design review: see the newest note, `…-stopped-design-review.md`.
