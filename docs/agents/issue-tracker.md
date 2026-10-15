# Issue tracker: GitHub

Tasks and specifications live in GitHub Issues for
`jokestorm/trading-workbench`. Use the `gh` CLI from the repository root.

## Operations

- Read: `gh issue view <number> --comments`
- List: `gh issue list --state open --json number,title,body,labels`
- Create: `gh issue create --title "..." --body-file <path>`
- Comment: `gh issue comment <number> --body-file <path>`
- Label: `gh issue edit <number> --add-label "<label>"`
- Remove label: `gh issue edit <number> --remove-label "<label>"`
- Close: `gh issue close <number>`

Use UTF-8 files for multiline bodies. Keep private statements, account
information, and credentials out of public issues.

“Publish to the issue tracker” means create a GitHub issue.
“Fetch the relevant ticket” means read the issue and its comments.

## Pull requests as a triage surface

**PRs as a request surface: no.**

## Wayfinding

Use a map issue labelled `wayfinder:map` and child issues labelled
`wayfinder:research`, `wayfinder:prototype`, `wayfinder:grilling`, or
`wayfinder:task`. Prefer native sub-issues and blocking dependencies.
If unavailable, use a task list in the map, a `Part of #<map>` reference
in each child, and `Blocked by: #<number>` references.

Choose unassigned children whose blockers are closed, in map order.
Claim with `gh issue edit <number> --add-assignee @me`.
Record the result, close the child, and update the map’s decisions.
