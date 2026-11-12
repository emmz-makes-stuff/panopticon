# Spec Delta

## Purpose

How Panopticon presents itself in the UI: the logo that goes with the `PANOPTICON` wordmark, and the icon a browser
shows for the site, so the product looks the same on every page and in every tab.

## ADDED Requirements

### Requirement: Logo beside the wordmark
Every dashboard page's header SHALL show the Panopticon logo immediately before the `PANOPTICON` wordmark, at about
the wordmark's height. Where the wordmark is a link home, the logo SHALL be part of that link. The logo SHALL be
decorative to assistive technology, so the header's accessible name stays "PANOPTICON".

#### Scenario: Signed-in page
- **WHEN** a signed-in member opens any page, such as the dashboard, Members or Tokens
- **THEN** the header starts with the logo, then the `PANOPTICON` wordmark, and selecting the logo goes home, just as
  selecting the wordmark does

#### Scenario: Sign-in page
- **WHEN** someone who isn't signed in opens the sign-in page
- **THEN** its header shows the logo before the wordmark too

#### Scenario: Screen reader
- **WHEN** a screen reader reaches the header's home link
- **THEN** it announces "PANOPTICON" once, with no extra name or description for the image

#### Scenario: Either colour scheme
- **WHEN** the page is shown in the light or the dark colour scheme
- **THEN** the logo appears with transparent corners, with no white or square edges around its rounded shape

### Requirement: Site icon
The dashboard SHALL declare the logo as its icon: a PNG favicon for browser tabs and an `apple-touch-icon` for home
screens, served from the dashboard's own origin without signing in.

#### Scenario: Browser tab
- **WHEN** someone opens the dashboard in a browser
- **THEN** the tab shows the logo, not a generic page icon

#### Scenario: Icons without a session
- **WHEN** a client with no session cookie requests the favicon or the touch icon declared by the page
- **THEN** the server returns the PNG with status 200, and not the sign-in page or a 401
