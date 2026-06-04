# GameServer Headless Architecture Invariants

### Runtime Core & Performance Configuration
*   **Dynamic Heartbeats via `ServerConfig`**: The loop must be completely game-agnostic, reading configurations at startup to toggle execution profiles:
    *   **Arena Match Profile**: Ultra-responsive **128Hz simulation ticker** (7.81ms target tick frames).
    *   **MMO Shard Profile**: Massively scalable **30Hz simulation ticker** (33.33ms target tick frames).
*   **Zero Heap Allocation Loop**: Memory instantiation inside the tick loops is forbidden. Use static array pools or permanent tracking dictionaries mapped cleanly to recycled struct indices.

### Critical Network Boundary Constraint (MTU Safety)
*   **Strict MTU Constraint (< 1,200 Bytes)**: Individual UDP packet transmissions must remain under 1,200 bytes to eliminate network layer IP fragmentation.
*   **Buffer Splitting Implementation**: For spatial grid iterations or cell brawls, if a nearby player packet array footprint pushes past 1,200 bytes, you must write an allocation-free array splitting routine that fragments the data array payload into multiple under-MTU packages.

### Physics-Free Simulation
*   **Pure Mathematical Hitboxes**: Bypasses traditional physics engines. Entities are defined as 2D/3D primitive mathematical geometry (cylinders, spheres, lines, or vectors). All collisions are evaluated using strict mathematical proximity functions.
