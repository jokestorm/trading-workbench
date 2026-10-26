# Trader chat

Open **Trader chat** in the sidebar. The conversation is saved on the Workbench
instance and uses its existing Codex ChatGPT sign-in. Subscription usage is shared
with other Codex work, including the continuous caller. No API key is required.

The .NET host runs a private `codex app-server` subprocess over stdio, creates a
persistent conversation on first send and resumes its recorded thread after
restart. App Server retains model history and compacts context. Workbench saves
the visible transcript and thread identity in `trader-chat/conversation.json`.
The page observes incremental output through status polling; observing or
refreshing the page never initiates inference. New messages use client UUIDs so
retries cannot duplicate a turn. Closing the page does not stop a reply. **Stop
reply** explicitly interrupts it. An interrupted app restart does not resubmit.

The agent has four application-owned data tools: filtered transaction performance,
allocated closed-trade fills, current market observations and caller context.
The market tool refreshes the quote and historical bars for a discovered contract;
errors remain explicit. Responses retain data timestamps. All account accounting
uses the transaction projection; CSV archive data does not contribute totals.
Chat cannot submit orders or change caller settings or positions.

Select a contract under **Discussing** to attach the exchange to its current trade
scope. Up to six completed messages from the preceding three hours, truncated to
1,600 characters each, join that scope's next caller analysis. Conversations for
other contracts and old position scopes are excluded. Account-wide discussion
remains in chat. Chat reserves the shared AI slot and interrupts background
caller inference or brief research that is already running. New background
inference waits until the reply finishes; quote polling, transaction tracking
and deterministic exit checks continue. Deferred caller work is reconsidered
with current observations rather than queued as an old request.

The transport uses App Server's experimental dynamic tool registration. Protocol
shapes were checked against the installed Codex JSON schema. Upgrade checks should
include thread creation/resumption, `item/tool/call`, streamed agent messages and
interruption. No socket listener is exposed. Existing local/HA ingress checks and
`/api/ai` cache controls apply to all chat routes.

`--trader-test` runs isolated synthetic checks without credentials or model usage.
Browser module checks cover escaping, polling and hidden views. A live integration
check separately verified ChatGPT authentication, account-tool access and a
remembered follow-up without modifying the user's conversation.
