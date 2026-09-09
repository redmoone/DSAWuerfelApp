# Mobile UI Refactoring Progress

## P0
Status: COMPLETE
Changed:
- DsaWuerfelApp/DsaWuerfelApp.Tests/Browser/browser-fixture.cjs
- DsaWuerfelApp/DsaWuerfelApp.Tests/Browser/app-workflows.cjs
- DsaWuerfelApp/DsaWuerfelApp.Tests/Browser/responsive-ui.cjs
- artifacts/mobile-ui-refactor/baseline-20260909 (ignored baseline screenshots)

Validation:
- dotnet build DsaWuerfelApp.sln -c Release -v minimal: PASS
- dotnet publish ... -v minimal: PASS
- app-workflows.cjs with Playwright/Three/Edge: PASS
- dice-lifecycle.cjs with Playwright/Three/Edge: PASS
- responsive-ui.cjs baseline: FAIL on known mobile overflow/menu gaps; run was stopped after baseline findings
- node --check browser scripts: PASS
- git diff --check: PASS

Notes:
- Node 24.13.0, Playwright 1.63.0, Three 0.160.0, Microsoft Edge available.
- Desktop references captured at 1440x900 for all four active routes with collapsed/expanded sidebar.
- No productive CSS/markup changes in P0; baseline mobile failures remain open for later phases.

## P1
Status: COMPLETE
Changed:
- DsaWuerfelApp/DsaWuerfelApp.Client/Layout/MainLayout.razor.css
- DsaWuerfelApp/DsaWuerfelApp.Client/wwwroot/app.css

Validation:
- dotnet build DsaWuerfelApp.sln -c Release -v minimal: PASS
- dotnet publish ... -v minimal: PASS
- app-workflows.cjs: PASS
- dice-lifecycle.cjs: PASS
- Shell geometry at 640px/641px: PASS
- Desktop sidebar widths 5.5rem/17rem and geometry: PASS
- git diff --check: PASS

Notes:
- Shell children now shrink with box sizing/min-width rules; no global overflow clipping was added.
- Full responsive matrix remains red only in later phase areas (mobile menu and child layouts).

## P2
Status: COMPLETE
Changed:
- DsaWuerfelApp/DsaWuerfelApp.Client/Layout/NavMenu.razor
- DsaWuerfelApp/DsaWuerfelApp.Client/Layout/NavMenu.razor.css
- DsaWuerfelApp/DsaWuerfelApp.Client/Layout/MainLayout.razor.css

Validation:
- dotnet build DsaWuerfelApp.sln -c Release -v minimal: PASS
- dotnet publish ... -v minimal: PASS
- app-workflows.cjs: PASS
- ClientStateTests: PASS (12/12)
- responsive-ui.cjs: P2 menu toggle/close/focus checks PASS; remaining failures are later page-layout baselines
- browser scripts syntax and git diff --check: PASS

Notes:
- One NavMenu/SessionTree instance remains mounted; mobile content is flow-based and desktop content can scroll at low height.
- Session ID comparison closes on actual session changes/logout while same-session refreshes leave the menu state alone.

## P3
Status: COMPLETE
Changed:
- DsaWuerfelApp/DsaWuerfelApp.Client/Components/SessionTree.razor.css

Validation:
- dotnet build DsaWuerfelApp.sln -c Release -v minimal: PASS
- dotnet publish ... -v minimal: PASS
- long-name/session actions in Lobby and mobile NavMenu: PASS
- rename, copy, delete-cancel and shared SessionTree geometry: PASS
- app-workflows.cjs: PASS
- ClientStateTests: PASS (12/12)
- git diff --check: PASS

Notes:
- Header actions wrap through one component container query; management and visibility parameters remain unchanged.
- Compact/coarse-pointer icon actions use the shared 44px target.

## P4a
Status: COMPLETE
Changed:
- DsaWuerfelApp/DsaWuerfelApp.Client/Components/AttributePill.razor
- DsaWuerfelApp/DsaWuerfelApp.Client/Components/AttributePill.razor.cs
- DsaWuerfelApp/DsaWuerfelApp.Client/Components/AttributePill.razor.css
- DsaWuerfelApp/DsaWuerfelApp.Client/Components/ModifierPill.razor.css
- DsaWuerfelApp/DsaWuerfelApp.Client/Components/TextPill.razor.css
- DsaWuerfelApp/DsaWuerfelApp.Client/Components/DiceControls.razor
- DsaWuerfelApp/DsaWuerfelApp.Client/Components/DiceControls.razor.css

Validation:
- dotnet build DsaWuerfelApp.sln -c Release -v minimal: PASS
- dotnet publish ... -v minimal: PASS
- P4a browser interaction/geometry check at 320px: PASS
- app-workflows.cjs: PASS
- dice-lifecycle.cjs: PASS
- ClientStateTests: PASS (12/12)
- git diff --check: PASS

Notes:
- Attribute add/remove now has separate semantic buttons; OnClick priority, right-click decrease and one-callback behavior remain intact.
- Dice controls are semantic buttons; modifier targets meet the shared 44px target and pill contents fit their narrow hosts.

## P4b
Status: COMPLETE
Changed:
- DsaWuerfelApp/DsaWuerfelApp.Client/Components/WuerfelActionBar.razor.css
- DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Wuerfel.razor.css

Validation:
- dotnet build DsaWuerfelApp.sln -c Release -v minimal: PASS
- dotnet publish ... -v minimal: PASS
- action-bar host widths around the 560px container query: PASS
- tooltip focus/width and prepared-state resize/sidebar check: PASS
- app-workflows.cjs: PASS
- dice-lifecycle.cjs: PASS
- ClientStateTests: PASS (12/12)
- git diff --check: PASS

Notes:
- Action-bar wrap ownership is consolidated in the component; measured page-level duplicate rules were removed after confirming the rendered component carries only its component scope.
- Narrow-host tooltip width is bounded by the action row while the disabled hidden-roll behavior remains unchanged.

## P4c
Status: COMPLETE
Changed:
- DsaWuerfelApp/DsaWuerfelApp.Client/Components/ProbenSearch.razor
- DsaWuerfelApp/DsaWuerfelApp.Client/Components/ProbenSearch.razor.cs
- DsaWuerfelApp/DsaWuerfelApp.Client/Components/ProbenSearch.razor.css

Validation:
- dotnet build DsaWuerfelApp.sln -c Release -v minimal: PASS
- dotnet publish ... -v minimal: PASS
- compact search flow and 320px geometry: PASS
- keyboard Tab/Shift+Tab with pauses over 150 ms: PASS
- main selection, input clearing, focus return and focus-leave close: PASS
- info-button interaction: PASS
- app-workflows.cjs: PASS
- dice-lifecycle.cjs: PASS
- ClientStateTests: PASS (12/12)
- git diff --check: PASS

Notes:
- Selectable result labels are sibling semantic buttons to the existing alternative buttons; inactive groups remain noninteractive text.
- Local focus generation keeps the dropdown open across delayed focus transitions and prevents stale blur closes after selection.
- The browser fixture has no special talent/spell alternative dataset; that alternative-specific proof remains a CN-4 validation item.

## P5a
Status: COMPLETE
Changed:
- DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Wuerfel.razor.css

Validation:
- dotnet build DsaWuerfelApp.sln -c Release -v minimal: PASS
- dotnet publish ... -v minimal: PASS
- normal/master grid geometry from 320px through 1920px: PASS
- prepared master attribute and normal probe state across resize/sidebar states: PASS
- responsive-ui.cjs: dice-grid overflow fixed; remaining lobby/control reachability failures documented below
- app-workflows.cjs: PASS
- dice-lifecycle.cjs: PASS
- ClientStateTests: PASS (12/12)
- git diff --check: PASS

Notes:
- The measured 320px action-grid minimum was capped to the available container width; desktop multi-column grids remain active where their measured space permits.
- No new container threshold or DOM hierarchy was introduced. The responsive harness still reports the pre-existing phone-320 lobby overflow and low-height control reachability cases, which belong to later page/validation work.

## P5b
Status: COMPLETE
Changed:
- DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Wuerfel.razor.css
- DsaWuerfelApp/DsaWuerfelApp.Client/Components/RollHistory.razor.css
- DsaWuerfelApp/DsaWuerfelApp.Client/Components/WuerfelInformationPanel.razor.css

Validation:
- dotnet build DsaWuerfelApp.sln -c Release -v minimal: PASS
- dotnet publish ... -v minimal: PASS
- 20-entry history and information panel across compact, landscape and desktop heights: PASS
- single history scroll owner and explicit Würfeln/history scroll reachability: PASS
- long-text wrapping rules and document overflow checks: PASS
- responsive-ui.cjs: 7 existing lobby/low-height control reachability failures; no P5b history overflow failure
- app-workflows.cjs: PASS
- dice-lifecycle.cjs: PASS
- ClientStateTests: PASS (12/12)
- git diff --check: PASS

Notes:
- The outer history host no longer scrolls; the component history container owns vertical history scrolling.
- Compact history and information hosts use bounded/natural content sizing while the desktop utility panel keeps its existing scroll behavior.
- The fixture has no special talent/spell alternative or master-result dataset; those scenarios remain CN-4 validation items.

## P6
Status: COMPLETE
Changed:
- DsaWuerfelApp/DsaWuerfelApp.Client/wwwroot/js/dice3d.js
- DsaWuerfelApp/DsaWuerfelApp.Client/wwwroot/js/dice-scene.js
- DsaWuerfelApp/DsaWuerfelApp.Tests/Browser/dice-lifecycle.cjs

Validation:
- dice-lifecycle.cjs with host-width/height changes, six-die row/scale thresholds, stable-width, same-width update, roll and hidden-host checks: PASS
- dotnet build DsaWuerfelApp.sln -c Release -v minimal: PASS
- dotnet publish ... -v minimal: PASS
- app-workflows.cjs with navigation/sidebar lifecycle: PASS
- ClientStateTests: PASS (12/12)
- git diff --check: PASS

Notes:
- Renderer/camera resizing remains in the existing render loop; the same loop and the existing window handler share an instance-local positive-width layout cache.
- Zero-sized hosts leave the last valid layout width intact and refresh layout when shown again; dispose still clears the cache with the existing animation/listener cleanup.

## P7a
Status: COMPLETE
Changed:
- DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Lobby.razor.css

Validation:
- dotnet build DsaWuerfelApp.sln -c Release -v minimal: PASS
- dotnet publish ... -v minimal: PASS
- anonymous lobby and invalid-auth feedback at 320px: PASS
- authenticated long-name create flow and board geometry: PASS
- invalid join feedback and geometry: PASS
- app-workflows.cjs: PASS
- dice-lifecycle.cjs: PASS
- ClientStateTests: PASS (12/12)
- git diff --check: PASS

Notes:
- Existing 1080px/760px breakpoints remain; narrow grid tracks, panel min-widths and compact board placeholder heights now fit the available host width.
- SessionTree remains the shared owner of session content and actions; no lobby-specific session markup or callbacks changed.

## P7b
Status: PARTIAL
Changed:
- DsaWuerfelApp/DsaWuerfelApp.Client/Pages/HeldenVerwaltung.razor.css

Validation:
- dotnet build DsaWuerfelApp.sln -c Release -v minimal: PASS
- dotnet publish ... -v minimal: PASS
- hero-management geometry at 320px through desktop widths: PASS
- long filename selection and invalid-extension validation: PASS
- app-workflows.cjs hero synchronization: PASS
- dice-lifecycle.cjs: PASS
- ClientStateTests: PASS (12/12)
- git diff --check: PASS

Notes:
- Narrow container/card tracks and selected-file names now fit and wrap within the host; existing InputFile and dropzone code was unchanged because no concrete interaction defect was reproduced.
- No valid importer fixture or native file-picker device run is available (CN-4/CN-5); valid-content upload and device drop behavior remain open validation items.

## P7c
Status: COMPLETE
Changed:
- DsaWuerfelApp/DsaWuerfelApp.Client/Pages/Kampf.razor.css

Validation:
- dotnet build DsaWuerfelApp.sln -c Release -v minimal: PASS
- dotnet publish ... -v minimal: PASS
- combat search, initiative/history, low-height layout and both desktop sidebar states: PASS
- shared attribute/modifier target geometry at compact and desktop widths: PASS
- app-workflows.cjs: PASS
- dice-lifecycle.cjs: PASS
- ClientStateTests: PASS (12/12)
- git diff --check: PASS

Notes:
- The combat shell can grow and scroll its local sidebar content at low heights; the search list is bounded locally and remains keyboard/touch usable.
- Combat page deep styles now preserve the shared 44px pill action targets, and long titles/action text wrap inside their panels.
