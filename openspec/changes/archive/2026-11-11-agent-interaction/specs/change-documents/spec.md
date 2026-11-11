# Spec Delta

## ADDED Requirements

### Requirement: Answers given from the dashboard
A change's page SHALL list the answers given to its sessions from the dashboard (see `remote-answers`, Answers are
attributed), newest first. Each entry SHALL show when, who answered, whose session it was, and the chosen labels, or
Allow or Deny. It SHALL be shown to every member who can see the change, but question text and chosen labels SHALL appear
only to members who could have answered.

#### Scenario: A lead's approval
- **WHEN** Bob allowed a permission prompt for Bash on Emmz's session yesterday
- **THEN** the change's page lists "Bob allowed Bash for Emmz", with the time

#### Scenario: A viewer
- **WHEN** Sam, an editor of `PAN`, opens the page of a `PAN` change with an answered question
- **THEN** Sam sees who answered and when, but not the question text or the answer chosen

#### Scenario: Live
- **WHEN** a change's page is open and someone answers one of its prompts
- **THEN** the new entry appears without a reload
