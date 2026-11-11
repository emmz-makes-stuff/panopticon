# 9.3: a body dropped mid-long-poll was already retried; the fix is defensive

<!-- Section: 9 · Task: 9.3 · Base: 4b7b371 -->

The new test, `A_wait_whose_connection_drops_mid_body_is_retried_and_keeps_waiting`, passed before any code change:

- `StubServer.DropAfterHeaders` sends the headers and a `Content-Length` of 64, then throws an `IOException` from the
  body stream.
- `HttpClient.SendAsync` uses `ResponseContentRead` by default. It buffers the body before returning, and wraps the
  `IOException` as an `HttpRequestException`.
- `AgentApiClient.SendAsync` already turns that into a `BridgeException`, which the wait loop retries.

So code review F6 and F7 can't happen as they read today: `ReadAsync` reads a buffered body and can only fail on JSON.

I still made the agreed change. `ReadAsync` now wraps an `HttpRequestException` or `IOException` as a
`BridgeException`, which keeps the class's promise that every failure is a `BridgeException` if a call ever streams
its body. The test guards the behaviour: a dropped wait is retried, and the answer still arrives.

## Next

- 9.4: a report's `change-updated` must not put back sessions that `session-ended` removed (web side).
