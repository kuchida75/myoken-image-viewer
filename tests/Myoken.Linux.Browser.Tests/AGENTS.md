# Browser policy regression scope

Read ../../linux/AGENTS.md. Run with bash linux/check.sh. Use temporary generated folders only. These console tests cover viewport arithmetic and directory enumeration, not GPU/desktop acceptance. Real X11 integration checks are in src/Myoken.Linux/BrowserRegression.cs and run through bash linux/smoke.sh with isolated state. Preserve the Windows source/build/version baseline.
