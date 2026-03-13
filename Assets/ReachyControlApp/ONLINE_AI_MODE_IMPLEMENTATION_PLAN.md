# Online OpenAI Mode Implementation Plan

## Goal

Add a new online AI mode that uses the OpenAI API alongside the existing local AI pipeline.

The current local AI path must remain available and visually familiar. The AI view should gain a separate mode toggle so the operator can switch between:

- `Local AI`
- `Online AI`

When `Local AI` is selected, keep the current panel behavior and settings intact.
When `Online AI` is selected, show a dedicated set of online-only controls and runtime diagnostics.

## Requested Product Decisions

This implementation guide follows these product decisions:

- Add a dedicated online mode toggle in the AI view.
- Keep the current local AI view behavior as-is.
- Allow direct joint commands from the model in V1.
- Do not require confirmation for model-triggered motion by default.
- Add a toggle that enables confirmation for model-triggered motion when desired.

## Current Baseline In This Repository

The repository already has the correct high-level architecture for this feature:

- Unity polls a local sidecar over HTTP through `VoiceAgentBridge`.
- The sidecar already exposes `/intent`, `/help`, `/speak`, and `/listening`.
- Unity already routes structured intents through `VoiceCommandRouter`.
- Motion execution already goes through connection checks, health checks, duplicate suppression, logging, and optional confirmation.

Relevant files:

- `Assets/ReachyControlApp/Scripts/ReachyRuntimeControlUI.cs`
- `Assets/ReachyControlApp/Scripts/VoiceAgentBridge.cs`
- `Assets/ReachyControlApp/Scripts/VoiceCommandRouter.cs`
- `Assets/ReachyControlApp/Scripts/VoiceTranscriptIntentParser.cs`
- `Assets/ReachyControlApp/LocalVoiceAgent/local_voice_agent_sidecar.py`
- `Assets/ReachyControlApp/voice_agent_config.json`
- `Assets/ReachyControlApp/LocalVoiceAgent/local_voice_agent_sidecar_config.json`

## Recommended Architecture

Keep STT and TTS local.
Add OpenAI integration inside the Python sidecar, not directly inside Unity.

This gives the project the smallest architectural change because Unity can continue talking only to the local sidecar, while the sidecar decides whether to use:

- the current local parser / local help path, or
- a new online OpenAI path

Recommended flow:

1. Microphone audio is handled locally by the existing STT path.
2. The final transcript is sent to the sidecar parser/orchestrator.
3. If AI mode is `Local`, keep the existing behavior.
4. If AI mode is `Online`, the sidecar sends the transcript plus robot context to OpenAI.
5. OpenAI returns a strictly structured response.
6. The sidecar validates the response and converts it into a structured Unity intent.
7. Unity executes the allowed action, optionally asks for confirmation depending on the new toggle, and uses local TTS for speech output.

## Important Design Rule

Do not implement motion control by parsing command tags from free text such as:

`(move head 30 degree left)`

That approach is fragile and makes speech output and command extraction fight each other.

Instead, require the model to return structured JSON with separate fields for:

- spoken reply text
- optional robot action

The TTS system should read only the spoken reply text.
The robot action should be handled separately by Unity.

## Proposed Online Response Schema

Use one action per turn in V1.
That keeps Unity-side changes smaller and is enough for direct joint commands from the first version.

Suggested schema:

```json
{
  "reply_text": "I am turning my head left.",
  "action": {
    "intent": "move_joint",
    "joint_name": "neck_yaw",
    "joint_degrees": 30.0
  },
  "confidence": 0.93
}
```

Allowed `action.intent` values in V1:

- `none`
- `help`
- `status`
- `connect_robot`
- `disconnect_robot`
- `set_pose`
- `move_joint`
- `show_movement`
- `stop_motion`

Suggested alternatives:

- For pure conversation, return `"action": null`.
- For pose commands, use `"intent": "set_pose"` with `pose_name`.
- For joint commands, use `"intent": "move_joint"` with `joint_name` and `joint_degrees`.

Do not support arbitrary multi-step action lists in V1.
If multi-action choreography is needed later, add `actions[]` in V2.

## AI View UX Plan

Add a new top-level AI mode selector in the AI view:

- `Use Local AI`
- `Use Online AI`

Behavior:

- When `Use Local AI` is selected, keep the current local AI controls visible and unchanged.
- When `Use Online AI` is selected, hide the local-model-specific controls and show online-mode-specific controls.

Recommended online controls:

- `Enable online AI`
- `OpenAI model`
- `API key env var name`
- `API key setup help`
- `Optional base URL`
- `Online system prompt`
- `Online request timeout`
- `Temperature`
- `Max output tokens`
- `Allow direct joint commands`
- `Require confirmation for online AI motion`
- `Simulation-only voice`
- `Block motion when bridge is unhealthy`
- `Test online connection`

Do not store the raw API key inside Unity asset JSON by default.
Prefer an environment variable name such as `OPENAI_API_KEY`.

Add a dedicated help button in the online AI section:

- `How to create and connect my OpenAI API key`

When the button is pressed, open an in-app help panel, modal, or foldout with the onboarding instructions described in the `API Key Setup Help Content` section below.

## Configuration Changes

Extend `Assets/ReachyControlApp/voice_agent_config.json` with new fields such as:

```json
{
  "ai_mode": "local",
  "online_ai_enabled": false,
  "online_ai_model": "",
  "online_ai_api_key_env_var": "OPENAI_API_KEY",
  "online_ai_base_url": "",
  "online_ai_timeout_seconds": 15.0,
  "online_ai_temperature": 0.2,
  "online_ai_max_output_tokens": 180,
  "online_ai_system_prompt": "You are Reachy's online conversational AI.",
  "online_ai_allow_direct_joint_commands": true,
  "online_ai_require_motion_confirmation": false
}
```

Extend `Assets/ReachyControlApp/LocalVoiceAgent/local_voice_agent_sidecar_config.json` with matching sidecar-facing fields.

Recommended optional config additions for the onboarding UI:

```json
{
  "online_ai_show_api_key_help_on_first_open": true,
  "online_ai_last_api_key_check_ok": false
}
```

## System Prompt Strategy

The system prompt should define:

- the robot persona
- the conversation context
- the allowed action schema
- strict safety boundaries
- the requirement to stay within the provided pose and joint allowlists

Prompt guidance:

- The model may answer conversationally.
- The model may emit one structured robot action in the allowed schema.
- The model must not invent joints or poses.
- The model must not emit actions outside the allowlist.
- If unsure, the model should return no action and ask a clarifying question.
- If the robot is disconnected or unhealthy according to provided state, the model should avoid motion actions.

## Direct Joint Commands In V1

Direct joint commands are explicitly allowed in V1.
Because of that, validation must be strict.

Required safeguards:

- Only accept joints from an allowlist.
- Clamp or reject degrees outside configured limits.
- Reject unknown joints.
- Reject malformed action payloads.
- Reject commands if simulation-only mode blocks them.
- Reject commands if the bridge or robot connection is unhealthy.

Important repository note:

The current default known-joint lists for the sidecar and config do not include neck joints even though the runtime already supports them.
Add at least:

- `neck_roll`
- `neck_pitch`
- `neck_yaw`

Recommended V1 joint allowlist:

- all currently supported arm joints
- both grippers
- neck joints

## Confirmation Policy

Requested behavior for this feature:

- Default: online AI motion does not require confirmation.
- Optional: an operator toggle can force confirmation for online AI motion.

Implementation detail:

Add a new online-only toggle:

- `Require confirmation for online AI motion`

When this toggle is `false`:

- Unity should execute valid online motion intents immediately after validation.

When this toggle is `true`:

- Unity should route online motion intents into the existing pending confirmation flow.

Recommended rule:

- Keep `stop_motion` confirmation-free regardless of this toggle.

## Unity-Side Changes

### 1. `ReachyRuntimeControlUI.cs`

Add:

- AI mode selector state
- online AI settings fields
- online AI section in the AI panel
- logic to save/load the new config fields
- runtime display for online backend health, last online reply, and last online validation result

Behavior changes:

- Preserve the local AI section without breaking current workflows.
- When online mode is active, configure the bridge/sidecar with online settings.
- When an online action arrives, apply the new online confirmation toggle.

### 2. `VoiceAgentBridge.cs`

Extend the payload model to include separate speech text from the online backend.

Suggested new fields on `VoiceAgentIntent` or its envelope:

- `reply_text`
- `source_backend`
- `source_mode`

Unity should:

- speak `reply_text` through the existing local TTS queue
- never read action metadata aloud

### 3. `VoiceCommandRouter.cs`

Keep using the existing route model, but add awareness of source mode if needed so the online confirmation toggle can be applied cleanly.

### 4. `VoiceTranscriptIntentParser.cs`

Keep this parser for local mode and as a fallback.
Do not use it as the primary online action extraction path.
Online mode should prefer validated structured responses from the sidecar.

## Sidecar Changes

### 1. Add an online orchestrator

Extend `local_voice_agent_sidecar.py` with a new online backend that:

- reads online config
- loads the API key from an environment variable
- calls the OpenAI API
- validates the structured response
- converts it into the Unity intent format

### 2. Preserve the current HTTP contract

Keep `/intent`, `/speak`, `/help`, and `/listening`.

Recommended online behavior:

- the sidecar still owns transcript handling
- the sidecar still queues events for Unity polling
- Unity should not call OpenAI directly

### 3. Add strict schema validation

Validation should happen before the event is added to the queue.

Validation steps:

1. Parse JSON.
2. Verify required fields.
3. Verify the action intent is allowed.
4. Verify pose or joint values against allowlists.
5. Normalize the payload into the existing Unity event shape.
6. If validation fails, replace the action with `null` and provide a safe reply text.

### 4. Keep local TTS

Even in online mode, keep speech output local through the existing `/speak` endpoint and Unity bridge flow.
This avoids cloud audio dependencies and keeps current robot speaker mirroring usable.

## OpenAI API Integration Recommendation

Use the OpenAI API from the sidecar with structured outputs or function-calling style responses.

Practical guidance:

- Use the current OpenAI Responses API path for new development.
- Keep the chosen model configurable from the UI.
- Keep the request timeout short.
- Limit output size.
- Prefer deterministic settings for action generation.

Do not hardcode a model name in code.
Keep it operator-configurable because model availability changes over time.

## API Key Setup Help Content

The online AI view should include a button that opens step-by-step instructions for creating and attaching the user's own OpenAI API key.

Suggested button label:

- `How to create and connect my OpenAI API key`

Suggested help content:

1. Open the OpenAI Platform website in your browser.
2. Sign in with your own OpenAI account.
3. Open the API keys page.
4. Create a new secret key.
5. Copy the key immediately and store it safely.
6. Set the key on this computer as an environment variable named `OPENAI_API_KEY`, or another name chosen in the app.
7. Return to the app and make sure the `API key env var name` field matches the environment variable name.
8. Press `Test online connection` in the app.

Suggested notes in the help content:

- ChatGPT and OpenAI API billing are separate products.
- The key must belong to the user who wants to pay for the API usage.
- Do not paste the key into shared project files.
- Do not send the key through chat or commit it to git.
- If the app does not detect the key, restart the app after setting the environment variable.

Suggested Windows-specific quick setup example shown inside the help content:

```powershell
[System.Environment]::SetEnvironmentVariable("OPENAI_API_KEY", "paste_your_key_here", "User")
```

Suggested follow-up line shown below the command:

- After setting the variable, close and reopen the Unity app or the built executable before testing the online connection.

Recommended help-panel actions:

- `Open OpenAI Platform`
- `Open API keys page`
- `Copy PowerShell example`
- `Re-check API key availability`

If direct browser links are added, keep them configurable or centralized in code so they can be updated later if needed.

## Online Prompt Inputs

Each online request should provide:

- system prompt
- latest user transcript
- AI mode
- whether confirmation is required
- whether the session is simulation-only
- robot connection state
- bridge health state
- known preset poses
- known joints
- joint degree limits

This context should be generated in the sidecar or pushed from Unity to the sidecar as needed.

## Data Flow Recommendation

Recommended V1 data flow:

1. User speaks.
2. Local STT produces final transcript.
3. Sidecar sees `ai_mode = online`.
4. Sidecar builds an OpenAI request with transcript and current control context.
5. Sidecar receives a structured online response.
6. Sidecar validates it.
7. Sidecar enqueues one normalized event for Unity.
8. Unity speaks `reply_text`.
9. Unity executes the validated action immediately or via confirmation depending on the online confirmation toggle.

## Logging And Diagnostics

Add online-specific diagnostics to the AI panel and sidecar logs:

- last online request timestamp
- last online response summary
- last validation failure reason
- current model name
- whether the API key env var was found
- last HTTP error
- online backend latency

Recommended onboarding diagnostics in the online AI section:

- `API key status: found / missing`
- `Env var name in use`
- `Last key check time`
- `Last connection test result`

Do not log the API key.

## Safety Notes

This feature intentionally allows direct joint commands and allows motion without confirmation by default.
That is acceptable only if validation remains stronger than the model.

The model is not the source of truth.
The source of truth remains Unity-side execution policy plus sidecar-side schema validation.

Minimum required enforcement:

- allowlisted intents only
- allowlisted joints only
- range validation
- unhealthy-bridge motion blocking
- simulation-only blocking
- immediate stop support
- structured logging

## Implementation Phases

### Phase 1: UI and config scaffolding

- Add the AI mode toggle in Unity.
- Add online config fields in Unity and JSON.
- Add online section rendering in the AI view.
- Add the API key help button and in-app onboarding instructions.

### Phase 2: Sidecar online backend

- Add OpenAI request code in the sidecar.
- Add environment variable API key loading.
- Add structured response validation.

### Phase 3: Unity event handling

- Extend the bridge payload with `reply_text` and source metadata.
- Route online actions into the existing action executor.
- Apply the online confirmation toggle.

### Phase 4: Joint allowlist completion

- Add neck joints to defaults.
- Verify runtime joint naming consistency.
- Confirm that online `move_joint` actions can target the same names that `ReachyGrpcClient` already supports.

### Phase 5: Diagnostics and tests

- Add parser/validator tests for online responses.
- Add manual tests for online motion with and without confirmation.
- Add timeout, invalid schema, and disconnected-robot tests.

## Minimum Acceptance Checklist

- Operator can switch between Local AI and Online AI from the AI view.
- Local AI view remains intact.
- Online AI view includes a button that explains how to create and connect a personal OpenAI API key.
- Online AI can return conversational replies without robot movement.
- Online AI can trigger `set_pose`.
- Online AI can trigger direct `move_joint` commands, including neck joints.
- Default online motion executes without confirmation.
- The operator can enable confirmation for online motion with a toggle.
- Invalid online payloads do not move the robot.
- Local TTS does not read command metadata aloud.
- Stop commands remain immediate.

## Recommended First V1 Demo Scenario

Use a narrow demo prompt and test these utterances:

- "Hello Reachy."
- "Look 20 degrees left."
- "Raise the right shoulder to 10 degrees."
- "Go to tray holding pose."
- "Stop."
- "Who are you?"

If those work reliably in both simulation and controlled real-robot testing, the feature is ready for broader iteration.
