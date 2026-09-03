---
name: playsphere-testing
description: xUnit, integration tests, media tests, Windows UI Automation and performance test conventions for PlaySphere.
---

# PlaySphere Testing

## Conventions
- Unit tests: pure logic, no UI thread (namespace `PlaySphere.UnitTests`).
- Integration tests must not mock the feature under test.
- Media/visualizer work adds golden tests; never replace a golden expected value with current broken output.
- UI changes: run Windows UI Automation tests and a manual high-DPI check.
- A ticket is complete only when tests are added and pass, build passes, cancellation and error paths work
  (PICKLE.md §56).
- Required commands before claiming completion: `dotnet restore`, `dotnet build -c Release`, `dotnet test -c Release`.