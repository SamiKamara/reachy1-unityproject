# Reachy 1 Controller and Simulator

**Upstream:** [Sami Kamara's project](https://github.com/SamiKamara/reachy1-unityproject) was used to extend these features.

This project was developed as part of the HTI.460 course, Social Robots: Design, Research and Interaction (March-May 2026). Reachy 1 was the primary robot used, serving as a platform to support and enhance participants' robot and AI literacy. The selected dimension within this literacy framework was "envisioning robots in the future", a recently introduced dimension developed by the course instructors, building on prior work by Ahtinen et al. (2025) in *Robotour for Seniors - A Co-Learning Model to Enhance Robot Literacy among Older Adults in the Care Home Context* (*International Journal of Social Robotics*, 17, 1201-1220). https://doi.org/10.1007/s12369-025-01277-8

| Symbol | Meaning |
|--------|---------|
| 🟢 | Indicates features tested on the physical robot and accurately reflected in the simulation |
| 🟡 | Indicates features not yet tested; real-world behavior may differ |

-----

### Features Added & Focused On:
`Animation Creator tab` 09.04.2026 🟢

This allows you to create, edit, save, import, export, and play custom animated poses for Reachy directly at runtime. It is useful for building new reactions, emotions, demos, and reusable motion sequences without hardcoding each animation first.

- `Scene posing + sliders`: Move Reachy's joints either with the joint sliders or by clicking and dragging the 3D scene avatar.
- `Drafts and Saved Animations`: New poses first appear in a draft list for the current session, while locally stored JSON animations appear in a separate saved list.
- `Keyframe-based editing`: Record full-body keyframes, switch between drafts, reopen saved animations for editing, and update them from the same tab.
- `Safe recording flow`: Keyframes can only be recorded when pose capture is active and the joints have actually been moved.
- `Local JSON library`: Save animations locally into the default `ReachyAnimations` folder, refresh them, export them elsewhere, and delete selected entries.
- `Import / Export`: Import one or multiple JSON animation files with schema validation, and export edited or saved animations to a user-chosen path.
- `Per-animation controls`: Play, stop, edit, update, save locally, and bulk-select animations directly from the lists.
- `UI feedback improvements`: Status messages, active/idle pose-capture indicators, selected-joint highlighting, and clearer editing state labels improve discoverability.
- `Mirror limb editing`: Toggle left/right limb mirroring during pose editing so paired arm joints can be adjusted symmetrically.
- `Example animation JSON`: A sample crab-dance animation is included at `Examples/ReachyAnimations/crab-dance.json`.

`Chit Chat tab` 14.04.2026 🟢

This allows you to have conversations with Reachy using text. Responses are sent via TTS. Useful in noisy environments and if STT fails.

- `Text chat`: Ask any questions to Reachy using mic or text and see your conversations.
- `Mic/Text Transcripts`: Log mic or text input into the chat bubbles, with the option to clear the chat or save it locally.
- `Topic buttons`: These are pre-defined topic buttons that will prompt Reachy to generate relevant dialogue e.g. tell me more about the future of robots in education.
- `Syncs with AI personas`: A chosen persona from AI Mode will still work e.g. coffee mug.
- `Interrupt Reachy`: With the Stop button, Reachy's movements and speech can be interrupted at any time.

`some features in AI Mode tab`

Added the following...
- Scrollwheel for the `Base prompt text box`.
- Multi-select delete checkboxes in `Browse saved personalities` to prevent duplicates.

### Videos

https://github.com/user-attachments/assets/8df364da-c103-4d8a-b9a3-8499f3dc0664

https://github.com/user-attachments/assets/aaa93e5c-b712-461a-8bcd-d636543b219e


### Notes & Possible Future Iterations

* The UX and UI can still be refined further, but the core Animation Creator workflow is now substantially more complete than the original runtime-only prototype.

* `Animation Creator tab` can be improved by adding:

  * ✅ permanent local saving of animation sequences as JSON files instead of runtime-only storage
  * ✅ separate `Draft` and `Saved Animations` lists for clearer session-vs-local workflow
  * ✅ import / export functionality for sharing and managing animations
  * ✅ editing and updating of existing saved animations from the list
  * ✅ per-animation play / stop controls and bulk selection tools
  * ✅ selected-joint highlighting to improve usability, with synchronization between the 3D avatar and corresponding UI controls
  * ✅ mirror left / right limb editing so paired joints (e.g. shoulders and arms) can move symmetrically during pose creation
  * ✅ status feedback improvements, including active / idle capture state, validation feedback, and clearer save / update guidance
  * sync and mapping with AI Mode so created animations can be directly linked to behaviors, emotions, or triggers
  * a 3D rotation XYZ control (gizmo / ball) to allow easier camera manipulation instead of a static front-facing view
  * support for attaching audio to specific animations for more expressive behavior
  * an option to save animations to a General (or Quick Actions) section for quick access

* `Chit Chat tab` can be improved by adding:

  * ✅ a transcript log that distinguishes between mic and text input (useful for verifying what input was successfully processed)
  * ✅ local saving of transcripts
  * ✅ ability to clear chats
  * pinning of specific chat bubbles for persistent memory and easy reference

* `AI Mode tab` can be improved by adding:

  * sync and mapping with the Animation Creator tab
  * Dance mode (to trigger predefined animation sequences such as Macarena, Crab Rave, SpongeBob Electric Zoo)
  * Tone control (e.g. optimistic, dystopian, realistic) to adjust response style and behavior
  * Max response control (in characters / tokens) to manage response length
