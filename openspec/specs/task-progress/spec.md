# task-progress Specification

## Purpose

Defines how a change's OpenSpec `tasks.md` is turned into per-section and overall progress, and how
the current section is chosen, so every view of progress agrees.

## Requirements

### Requirement: Sections
Each level-2 heading (`## …`) in `tasks.md` SHALL start a section. A section's number SHALL be the
leading number in its heading (`## 2. Core` → 2), and its title SHALL be the rest of the heading.
Headings without a leading number SHALL be numbered by position.

#### Scenario: Numbered headings
- **WHEN** `tasks.md` has headings `## 1. Setup` and `## 2. Core Implementation`
- **THEN** there are two sections: 1 "Setup" and 2 "Core Implementation"

#### Scenario: Unnumbered heading
- **WHEN** the third level-2 heading reads `## Workflow follow-up`
- **THEN** it is section 3, titled "Workflow follow-up"

### Requirement: Task counting
A task SHALL be a Markdown list item that starts with a checkbox. A box holding only `x` (either case,
any spacing) SHALL count as done. Every other marker SHALL count as not done. List items without a
checkbox SHALL NOT be counted.

#### Scenario: Done markers
- **WHEN** a section has `- [x] a`, `- [X] b`, `- [ x] c` and `- [ ] d`
- **THEN** that section has 3 of 4 tasks done

#### Scenario: Other markers are unfinished
- **WHEN** a section has `- [~] a`, `- [-] b` and `- [] c`
- **THEN** that section has 0 of 3 tasks done

#### Scenario: Plain bullets ignored
- **WHEN** a section contains `- Archive the change` with no checkbox
- **THEN** that line is not counted as a task

### Requirement: Overall progress
Overall progress SHALL be done tasks over total tasks across the whole file, including any tasks that
come before the first section.

#### Scenario: Overall across sections
- **WHEN** section 1 has 4 of 4 done and section 2 has 1 of 6 done
- **THEN** overall progress is 5 of 10

#### Scenario: No tasks
- **WHEN** `tasks.md` contains no checkbox items
- **THEN** overall progress is 0 of 0, which views show as empty, not as an error

### Requirement: Current section
The current section SHALL be the section number the agent reported, if that section exists. If more
than one section has that number, a section whose heading gives the number explicitly SHALL be chosen
over one numbered by position. Otherwise it SHALL be the first section with an unfinished task. If
every task is done, it SHALL be the last section that has tasks.

#### Scenario: Reported section wins
- **WHEN** the agent reports section 3 and section 2 still has unfinished tasks
- **THEN** the current section is 3

#### Scenario: Explicit number preferred
- **WHEN** `tasks.md` has headings `## 1. Setup`, `## Notes` (section 2 by position) and `## 2. Core`,
  and the agent reports section 2
- **THEN** the current section is 2 "Core"

#### Scenario: Unknown reported section
- **WHEN** the agent reports section 9 and `tasks.md` has 4 sections
- **THEN** the current section is the first section with an unfinished task

#### Scenario: Nothing reported
- **WHEN** the agent reports no section, section 1 is complete and section 2 is not
- **THEN** the current section is 2

#### Scenario: All done
- **WHEN** every task in every section is done
- **THEN** the current section is the last section that has tasks

### Requirement: Untrusted content
Parsing SHALL treat `tasks.md` as untrusted. Embedded HTML SHALL NOT be passed through to any rendered
output, and oversized input (over 1 MiB) SHALL be rejected.

#### Scenario: HTML in a task
- **WHEN** a task line contains `<script>alert(1)</script>`
- **THEN** the task is counted, and no script element reaches the dashboard

#### Scenario: Oversized file
- **WHEN** a report carries a `tasks.md` larger than 1 MiB
- **THEN** the report is rejected with an error stating the size limit
