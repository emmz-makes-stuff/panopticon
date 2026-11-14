## ADDED Requirements

### Requirement: Visit requests
A session's agent SHALL be able to ask its member to visit a URL with the bridge's `request_visit` tool (see
`cli-bridge`, Requesting a visit). While the member is away, the request SHALL be held as a prompt is (see A prompt
while away). Who may answer it, and when it is released, SHALL follow the rules for prompts (see Who may answer, and
Releasing a prompt).

#### Scenario: Away, PR to review
- **WHEN** Emmz is away and her session calls `request_visit` with `https://github.com/emmz/pan/pull/12`, the reason
  "Review the PR" and the action "Approve it if you're happy"
- **THEN** her card shows "Needs you" with the reason, the action and the link, and the session waits

### Requirement: Visit requests not held
A visit request SHALL NOT be held when its member isn't away, when its session has no card, or when it breaks the
visit limits. The tool SHALL then return at once, without waiting.

#### Scenario: No card
- **WHEN** Emmz is away and a session whose change has no card calls `request_visit`
- **THEN** the tool returns at once, telling the agent to ask in the terminal

#### Scenario: Not away
- **WHEN** Emmz isn't away and her session calls `request_visit`
- **THEN** the tool returns at once, telling the agent to ask in the terminal and to put the URL in the question

#### Scenario: Back at the desk
- **WHEN** a visit request is held and Emmz runs `panopticon away off`
- **THEN** within a few seconds the tool returns, telling the agent to ask in the terminal, and the card no longer
  offers to answer it

### Requirement: Visit request form
The dashboard SHALL show a held visit request with its reason, its action when it has one, and the URL in full as a
link. The link SHALL open in a new tab, with no access to the dashboard's window and no referrer. The form SHALL offer
**Done** and **Can't**, with an optional note of at most 1,000 characters. The session SHALL get the outcome, the note,
and who answered, via Panopticon.

#### Scenario: Done with a note
- **WHEN** Emmz opens the link, approves the PR, and answers Done with "approved, merge when green"
- **THEN** the session is told the visit was done, with that note, answered by Emmz via Panopticon

#### Scenario: Can't
- **WHEN** Bob, an owner of `PAN`, answers Can't with "no access to that repo"
- **THEN** the session is told the visit couldn't be done, with that note, answered by Bob via Panopticon

#### Scenario: The link
- **WHEN** Emmz selects the link on a held visit request
- **THEN** the URL opens in a new tab, and the dashboard stays open with the form

### Requirement: Visit request limits
A visit request SHALL be held only when its URL is an absolute `http` or `https` URL of at most 2,048 characters with
no user name or password in it, its reason is 1 to 500 characters, and its action, when given, is at most 1,000
characters. The hook SHALL check these before posting, and the server SHALL check them again.

#### Scenario: Not a web URL
- **WHEN** an agent calls `request_visit` with `file:///Users/emmz/report.html`
- **THEN** the tool returns an error saying only `http` and `https` URLs can be visited, and nothing is sent

#### Scenario: Credentials in the URL
- **WHEN** an agent calls `request_visit` with `https://bob:secret@example.com/`
- **THEN** the tool returns an error, and the URL never leaves the machine

### Requirement: What others see of a visit request
A visit request's URL, reason and action SHALL be shown only to members who may answer it. Every other member who can
see the change SHALL see only that the session needs its member to visit a page.

#### Scenario: A viewer
- **WHEN** Emmz's session holds a visit request, and Sam (an editor of `PAN`) views its card
- **THEN** Sam sees "Needs Emmz: visit a page", and no part of the URL, reason or action reaches Sam's browser

### Requirement: Visit answers are attributed
Every visit request answered from the dashboard SHALL be recorded with its change, the session's member, who answered,
when, the URL, and Done or Can't. The note SHALL NOT be recorded. The URL SHALL be shown in the record only to members
who could have answered it.

#### Scenario: Recorded
- **WHEN** Emmz answers a visit request for `https://github.com/emmz/pan/pull/12` with Done
- **THEN** the change's answers list "Emmz visited https://github.com/emmz/pan/pull/12 for Emmz: done", and Sam sees
  only "Emmz answered a visit request for Emmz"
