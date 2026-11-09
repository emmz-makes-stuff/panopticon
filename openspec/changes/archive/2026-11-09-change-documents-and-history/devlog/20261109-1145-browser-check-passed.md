# Browser check of 10.1 passed; archiving

<!-- After the change-done note · Head: 3a82694 -->

Emmz rebuilt and restarted the server, ran the 10.1 browser check, and said "passed":
- switching documents makes only the document request: no change refetch, and no new event stream;
- a hash-only navigation does nothing;
- Back and Forward switch the article.

Emmz also restarted `claude`, so this session's `panopticon mcp` is the current build, with the §7.1 lock and the
§8.1 retry. I report `change-documents-and-history` once, so the new bridge's watcher follows it. Then I run
`/opsx:archive`, with the dashboard and this change's page open (§6 recipe step 3).

## Next

- `/opsx:archive`, then check that the card left the dashboard and that History lists the change.
