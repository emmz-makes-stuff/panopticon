## MODIFIED Requirements

### Requirement: Needs you
A card with a session waiting on a question, permission prompt or visit request SHALL say so. To a viewer who may
answer it (see `remote-answers`, Who may answer), it SHALL say "Needs you" and offer the prompt. To anyone else, it SHALL
name the member it needs. A session waiting after its turn ended SHALL NOT count as needing anyone.

#### Scenario: Owner's view
- **WHEN** Emmz's session holds a question while she is away
- **THEN** her dashboard shows "Needs you" on that card, with the question's form

#### Scenario: Not away
- **WHEN** Emmz isn't away and her session asks a question
- **THEN** her card shows "Needs you", with the question and "answer in the terminal", and no form

#### Scenario: A teammate's view
- **WHEN** Sam, an editor of `PAN`, views the same card
- **THEN** it shows "Needs Emmz: question", with no form and no question text

#### Scenario: A visit request
- **WHEN** Emmz's session holds a visit request while she is away
- **THEN** her dashboard shows "Needs you" on that card, with the visit request's form, and counts it in the tab
  title
