# Stage Execution Prompts

Each file contains a self-contained execution prompt for exactly one roadmap unit.
Copy its text block into a session, or request: "Execute only M00 using
`docs/prompts/M00.md`." The prompt instructs the agent to load current policies and
approved designs rather than relying on previous conversation memory.

Start with **[M00](M00.md)**. It is a proposal, not permission to implement.
When approval is needed, answer the proposal explicitly and then request resuming
the same unit. Broad approval of the roadmap is not approval of unseen APIs.

| Prompt | Work unit |
| --- | --- |
| [M00](M00.md) | Initial scope and contracts proposal |
| [M01](M01.md) | Solution skeleton |
| [M02](M02.md) | Binary primitives |
| [M03](M03.md) | Raw TIFF values |
| [M04](M04.md) | TIFF EXIF reading |
| [M05](M05.md) | EXIF layout planning |
| [M06](M06.md) | EXIF serialization |
| [M07](M07.md) | JPEG scanning |
| [M08](M08.md) | JPEG EXIF editing |
| [M09](M09.md) | Reference tests and first release audit |
| [M10](M10.md) | XMP packets and sidecars |
| [M11](M11.md) | JPEG XMP |
| [M12](M12.md) | IPTC IIM and Photoshop IRB |
| [M13](M13.md) | PNG metadata |
| [M14](M14.md) | WebP metadata |
| [M15](M15.md) | Standalone TIFF reading |
| [M16](M16.md) | Restricted TIFF editing |
| [M17](M17.md) | DNG feasibility only |
| [M18](M18.md) | Performance and packaging audit |

Later prompts are precise scope boundaries, not a claim that future technical
choices have already been settled. Update a future prompt after an approved design
change, keeping the roadmap consistent. If a unit proves too large, stop and
propose a split with independent gates before implementing it.
