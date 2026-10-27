# Review validation

Review date: October 26 2026. User environment: Eclipse v18.

- Verified every extracted original file byte for byte against CsiAutoPlan_11.zip: **30 files unchanged**.
- Project contains **22 C# source files**; the navigation index lists **205 method declarations**.
- The original review validated local document/source links and source-line bounds. Publication links have been converted to portable relative GitHub links with source line anchors.
- Compiled five unchanged ESAPI-independent Core source files and ran **18 passing offline checks** with offline_core_checks.ps1. The checks use synthetic numerical inputs, not patient data.
- Read the procedure's text and tables and inspected embedded screenshots relevant to geometry and uncertainty settings. Procedure references use extracted paragraph IDs, not inferred Word page numbers.
- The proprietary ESAPI reference assemblies and the separately referenced original verification package are absent from the supplied archive.
- The full plugin and new ESAPI teaching examples were **not compiled or run against Eclipse**. There was no live patient access, dose calculation, treatment-unit validation or clinical commissioning.

## Learning examples

The examples folder is outside the original SDK project. Its C# file is not automatically included in the supplied plugin. To try it, copy it into a development copy of the project, compile with the matching v18 references, increment the version and test through the local development workflow described in [START_HERE.md](START_HERE.md).

## Package contents

The repository includes the unchanged original project, the walkthrough and companion notes, a declaration index, read-only teaching methods and the offline check script. The original clinical procedure, extracted text, screenshots, authoring tools and local ZIP package are excluded. Procedure paragraph labels refer to the supplied clinical source. Documentation links use portable relative paths and GitHub line anchors.
