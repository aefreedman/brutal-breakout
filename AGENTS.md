# Project working agreements

- Use feature branches; do not merge into `main` without the user's approval.
- Use focused subagents for implementation and independent review. Keep one writer per working tree; reviewers are read-only.
- Make small atomic commits: separate independently valid behavior changes from workflow/docs wiring, and build/test each before committing.
- Keep Git remotes on HTTPS.
- Planning files belong in `C:\Users\aaron\obs-mono\vaults\personal\brutal-breakout`, using `YY-MM-DD-kebab-case.md` names.
- Check published `Ahwoo.Brutal.*` packages and their actual APIs before writing custom functionality. Prefer suitable BRUTAL types/helpers; record genuine gaps and avoid duplicating existing facilities.
- Use Vulkan directly. Keep the renderer small and simulation independent of platform/graphics code.
- Favor data-oriented simulation with explicit synchronous systems. Use async/await for loading/orchestration with explicit thread affinity, not one task per ball.
- Treat `C:\Users\aaron\dev\PrototypeStudio\PrototypeStudio` as a read-only gameplay/visual reference. Preserve desired catch/aim/relaunch interaction, not every mode or the phone portrait constraint. Do not assume a paddle.
- Build and run the bounded `--smoke-test` after rendering lifecycle changes. SDK validation layers are required for this hidden GPU check. Visible lifecycle/stress checks require separate explicit user permission; never launch them automatically.
