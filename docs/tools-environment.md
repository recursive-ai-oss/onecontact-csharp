# Tool environment

The scripts and tools under `tools/`, `recipes/` and `bench/` keep everything large outside the repository. Each location is an environment variable with a default under `~/onecontact/`; nothing in the repository names a machine or a private folder.

| Variable | Default | Holds |
|---|---|---|
| `ONECONTACT_DATA` | `~/onecontact/data` | Downloaded registers and name lists (`train/<name>`), rendered training sets (`rendered/<group>`, for example `rendered/tier2-a`, `tier2-b`, `tier2-c-north`, `tier2-c-south`, `tier2-c-nordic`, `tier2-c-east`, `tier2-au`), challenger data (`challenger/`). |
| `ONECONTACT_WORK` | `~/onecontact/work` | Scratch for runs: bench snapshots and results (`bench/`), quick trial renders (`tier2-east/`), saved models (`models/tier1.onenlp`). |
| `ONECONTACT_EVAL` | `~/onecontact/eval/private` | Evaluation sets (`tier2/group-a`, `group-b`, `c-north`, `c-south`, each with `sets/`; `sets/` for US and CA; `fresh/names`, `fresh/ro-mx`, `fresh/addresses` for the sets drawn for the 1.0 gate). Private: they hold personal data and stay on the machine that holds them. |
| `ONECONTACT_ORACLES` | `~/onecontact/oracles` | Pinned oracle installs (jars, JDK, Python environments). |
| `ONECONTACT_UPSTREAM` | `.upstream/` in the checkout | Upstream data files fetched by `tools/fetch/all.sh`. |
| `ONECONTACT_ARTIFACTS` | `artifacts/` in the checkout | Build output. |
| `ONECONTACT_LOCAL_FEEDS` | unset | Extra NuGet sources (semicolon separated) for Recursive AI preview packages. |
| `ONECONTACT_TIER1_MODEL`, `ONECONTACT_MODELS_MODEL`, `ONECONTACT_WEB_MODEL`, `ONECONTACT_TIER1_WEIGHTS` | unset | Model files for tests, evaluation and packing. |
| `ONECONTACT_TIER2_OUT`, `ONECONTACT_TIER2B_OUT`, `ONECONTACT_TIER2B_DATA` | under `~/onecontact/` | Output and input folders of the recovery tools. |

Scripts assume `dotnet` (.NET 10 SDK) is on `PATH`.
