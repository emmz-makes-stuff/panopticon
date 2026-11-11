# Spec Delta

## ADDED Requirements

### Requirement: The Project page's tags are live
While a member has a Project's page open, its Tags list SHALL show that Project's tags as they are now, with their
change counts, after any of them is added, renamed or deleted by anyone, with no reload. This SHALL hold for every role
and SHALL leave the rest of the page as it is.

#### Scenario: A rename by someone else
- **WHEN** Sam has `PAN`'s page open, and Emmz renames `PAN/area: onboarding` to `area: first-run` from another browser
- **THEN** Sam's Tags list shows `PAN/area: first-run` in its sorted place with its count, without a reload

#### Scenario: An add from a change's page
- **WHEN** a viewer has `PAN`'s page open, and an editor adds `PAN/bug` from a change's tag picker
- **THEN** the viewer's list shows `PAN/bug` with "1 change"

#### Scenario: A delete
- **WHEN** Sam has `PAN`'s page open, and Emmz deletes `PAN/bug`
- **THEN** `PAN/bug` leaves Sam's list

#### Scenario: Another Project's tags
- **WHEN** Sam has `PAN`'s page open, and a tag of `ACME` changes
- **THEN** Sam's page doesn't change

#### Scenario: The rest of the page stays put
- **WHEN** Emmz has typed into the add-tag input or the invite form on `PAN`'s page, and Sam adds a tag to `PAN`
- **THEN** Emmz's list shows the new tag, and what Emmz typed stays where it was

### Requirement: A rename in progress survives live updates
While a member is renaming a tag on the Project page, a live update SHALL keep that tag's rename form open, with what
they typed. If that tag is deleted meanwhile, its row SHALL go, and the page SHALL say it was deleted.

#### Scenario: Another tag changes
- **WHEN** Emmz is renaming `PAN/bug` and has typed `defect`, and Sam renames `PAN/area: billing`
- **THEN** Emmz's rename form is still open with `defect`, and the other row shows Sam's new name

#### Scenario: The tag being renamed is deleted
- **WHEN** Emmz is renaming `PAN/bug`, and Sam deletes it
- **THEN** the form closes, the row goes, and the status line says `PAN/bug` was deleted

### Requirement: The Project page catches up after a reconnect
When the Project page's live stream drops and reconnects, the page SHALL refetch the Project's tags once, since
changes may have been missed. Leaving the page SHALL close its stream.

#### Scenario: Missed while offline
- **WHEN** Sam's connection drops, Emmz adds `PAN/bug`, and Sam's stream reconnects
- **THEN** Sam's list shows `PAN/bug` without a reload

#### Scenario: Counts are not live
- **WHEN** Sam has `PAN`'s page open, and Emmz puts the existing `PAN/bug` on another change
- **THEN** Sam's count for `PAN/bug` doesn't change until the list next changes, the stream reconnects, or Sam reloads
