# Cloud compatibility testing — runbook skeleton (P1)

Spec §18. **Status: skeleton.** No cloud resources exist, no IaC has been written, and nothing here has been run. Free-tier terms, Windows-client licensing and costs below are **unverified** (`docs/FACTS.md`); verify each on the provider's current pages before creating anything.

## Rules
- The **owner** creates and owns every cloud account. The agent never creates accounts, enters payment or identity details, accepts terms, or spends money. It prepares runbooks and templates for the owner to read before running.
- Nothing outside the free allowance, and no GPU SKU, without the owner approving the stated cost in writing.
- Results are labeled **cloud-VM** in `COMPATIBILITY.md` and never merged with physical-hardware results.

## What a cloud VM can and cannot show
| Can validate | Cannot validate (stays "untested" unless the owner supplies hardware) |
|---|---|
| Windows version, edition and display-language matrix | Real GPUs and vendor APIs |
| Standard-user vs admin parity (a real standard account exists in a VM) | Game frame pacing, anti-cheat behaviour |
| Installer, uninstall, self-contained package | USB / HID / Bluetooth / Raw Input hardware |
| Unit, integration and portability suites; VM-detection path | Wi-Fi, laptop battery/thermal/hybrid-GPU behaviour |
| No-GPU / no-battery / low-core / low-RAM code paths (small sizes) | Perceived stutter |
| Present-event capture from the harness on the software renderer (pipeline only, not GPU timing) | |

## Test matrix (target)
Windows 11 current, Windows 11 previous, Windows 10 22H2; Home/Pro (as obtainable); at least one non-English display language; each as standard user and as admin; small size (2 vCPU, 4–8 GB) and a larger size; AMD and Intel host CPUs where offered.

## Guardrails (record evidence for each run)
1. Budget alert at the lowest available threshold — screenshot or export.
2. Auto-shutdown schedule on every VM — evidence.
3. Resource tags (`project=jankscope`, `owner`, `expires`) on everything.
4. Deallocate when idle; delete the whole resource group after the campaign — evidence of deletion.
5. No secrets in the repository; credentials stay with the owner.
6. Windows 10/11 client images may require specific licensing on a trial subscription; otherwise use a server image as a compatibility proxy and label it "not a supported product OS" (**verify**).

## Per-run procedure (to be turned into a script in Stage 5)
1. Owner creates the VM from the approved image/size; confirms guardrails 1–3.
2. Copy the release candidate zip (no installer download from the internet).
3. Do not build on the VM. Run the packaged `jankscope probe --json` and the packaged test bundle twice: once as an administrator and once as a standard user.
4. Save the JSON capability matrix and test results next to the run record.
5. Owner deallocates/deletes; record evidence for guardrail 4.

## Complement without cloud cost
Microsoft evaluation ISOs in local VMs need a hypervisor; the reference bench is Windows Home (no Hyper-V host), so this needs the owner's approval to install one.
