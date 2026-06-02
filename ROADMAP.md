# ArenaMMOCore Development Roadmap

This document outlines the engineering architecture and milestone progression for building a high-performance, server-authoritative Action PvP MMORPG. The technical stack focuses on a **Headless .NET Console Server (30Hz)** and a **Unity DOTS/ECS Client**, designed to deliver maximum responsiveness on low-end hardware (e.g., target markets in South America) by achieving flatline memory performance and zero garbage collection (GC) pauses.

---

## 🏗️ Phase 1: The Absolute Core (Networking & Spatial Simulation)
**Goal:** Establish a rock-solid, zero-allocation foundation for client-server communication, movement interpolation, and spatial scalability up to 2,000 Concurrent Users (CCU).

### 1.1 Automated Shared Data & Serialization
*   **Goal:** Compile common data definitions once and share them instantly between the server and the Burst-compiled Unity client.
*   **Libraries:** `MemoryPack`
*   **Deliverables:** Blittable structs for inputs, spatial states, and RPC events. Zero-allocation `NetworkSerializer` utility class utilizing automated `.csproj` copy pipelines.

### 1.2 High-Precision 30Hz Simulation Loop
*   **Goal:** Prevent time drift and frame judder across server operating systems without burning 100% CPU thread cores.
*   **Libraries:** `System.Diagnostics.Stopwatch`
*   **Deliverables:** Headless C# console application driven by a tight, spin-locked frame accumulator loop with precise 33.33ms intervals.

### 1.3 Client-Side Snapshot Interpolation
*   **Goal:** Smooth out the server's 30Hz heartbeat ticks into a fluid 60Hz/144Hz+ visual stream on low-end monitors.
*   **Libraries:** Unity Mathematics (`math.lerp`)
*   **Deliverables:** `SnapshotElement` entity buffer arrays (`IBufferElementData`) and `NetworkMovementSystem` to playback historical points behind an intentional 100ms jitter buffer.

### 1.4 Input Prediction & Reconciliation (Micro-Stutter Protection)
*   **Goal:** Provide local players with immediate, lag-free responsiveness while enforcing strict server authority.
*   **Libraries:** `SmoothNet` or custom circular state buffers
*   **Deliverables:** Local input command tracking loops. Server-side validation loops checking predicted states against absolute calculations using a customizable *Permissible Error Threshold* (e.g., <15cm bends, >1m rubber-band snaps).

### 1.5 2D Spatial Grid (Interest Management for 2,000 CCU)
*   **Goal:** Mitigate the $O(N^2)$ networking bandwidth bottleneck by dropping bandwidth throughput from an impossible 38 Gbps to a manageable <1 Gbps.
*   **Libraries:** `NetStack.Aggregation`
*   **Deliverables:** A coordinate-to-cell mapping engine. The server divides worlds into 50x50m grid structures, restricting state packet distribution solely to entities inside a player's direct 9-cell visibility quadrant.

---

## 🔐 Phase 2: Session Management, Persistence & Economy Foundations
**Goal:** Handle player authentication, seamless secure handoffs to zone instances, database persistence, and synchronous stat computation via item equipment loops.

### 2.1 Login Server & Match Matchmaking/Lobby Handshake
*   **Goal:** Guard your high-speed simulation nodes from heavy authentication overhead and handle secure zone entry.
*   **Libraries:** ASP.NET Core Web API, `JWT (JSON Web Tokens)`
*   **Deliverables:** A lightweight HTTPS gateway server. Players log in, fetch a cryptographically signed token, query a lobby system, and present that token to the LiteNetLib socket via `ConnectionRequest.AcceptIfKey()`.

### 2.2 Entity Persistence Layer & Cache
*   **Goal:** Save and load player character arrays, stats, and equipment positions without stalling the high-frequency network tickers.
*   **Libraries:** PostgreSQL or MongoDB, Dapper / Entity Framework Core (Async only), Redis (optional cluster caching)
*   **Deliverables:** Headless database gateway layer operating strictly on independent asynchronous threads. On login, data maps to an active in-memory `ServerPlayer` structure.

### 2.3 Zero-Allocation Inventory & Loot Dropping
*   **Goal:** Manage looting actions and inventory storage matrices safely without triggering garbage collection heap spikes.
*   **Libraries:** `MemoryPack`, `NetStack.Serialization`
*   **Deliverables:** Fixed-size item ID collections (`uint[]` arrays mapping to slots) inside the `ServerPlayer` model. Item pickups on the ground represented as minimal mathematical bounding shapes containing unique metadata tags.

### 2.4 Mid-Combat Equipment Swapping & Dynamic Stat Re-computation
*   **Goal:** Allow players to switch loadouts in the middle of a battle, recalculating base attributes instantly while preventing memory thrashing.
*   **Libraries:** Pure C# Math Structs
*   **Deliverables:** An attribute modifier calculator running within the shared math layout. Changing an item triggers a bitmask change, re-applying item modifiers to the player's core attributes (`MaxHealth`, `AttackSpeed`, `BaseDamage`) inside a single cache-friendly frame loop.

---

## ⚔️ Phase 3: Action Game Mechanics & Combat Simulation
**Goal:** Engineer a deterministic, lag-compensated action combat framework that supports complex spell loops, status effects, and projectile behaviors.

### 3.1 Server-Side Lag Compensation (Hit-Scan Validation)
*   **Goal:** Ensure fast-paced projectile and melee accuracy feels completely fair to players operating under volatile latency profiles (e.g., 100ms+ ping).
*   **Libraries:** `netcode-lag-compensation` (or raw custom circular structure tracking)
*   **Deliverables:** The server caches a 300ms ring buffer history of all entity spatial coordinates. When a player casts a hit-scan capability, the server winds back the target's positions to the exact millisecond time stamp of the shot to validate the interaction.

### 3.2 Dynamic Spatial Projectile Trajectories
*   **Goal:** Simulate high-velocity projectile entities across the network without relying on heavy overhead object tracking or Unity physics.
*   **Libraries:** Unity Mathematics (`float3`)
*   **Deliverables:** Projectiles treated as flat math vectors on the server. Tracking metrics feature:
    *   **Pierce**: Simple entity check counters (`HitCount < MaxPierce`).
    *   **AoE / Explosion**: Circle-to-point radius proximity validations (`math.distance(projectile, player) <= explosionRadius`).

### 3.3 Status Modification Core (Buffs, Debuffs, Dots, and HoTs)
*   **Goal:** Run thousands of ticks of damage-over-time, attribute alterations, and curses in a highly optimized entity loop.
*   **Libraries:** Fixed-Size Struct Buffers
*   **Deliverables:** Active effects tracked as a fixed array inside the player struct (e.g., maximum of 8 active effect slots to ensure data layout remains blittable). Every server tick updates remaining durations and evaluates numerical alterations directly.

### 3.4 Synergistic Combat Loops (Lifesteal & Curses)
*   **Goal:** Implement complex, intertwined combat interactions (e.g., healing a player through a Damage-over-Time curse applied to an enemy) via high-performance linear execution paths.
*   **Libraries:** Core Shared Mathematics
*   **Deliverables:** Direct reference passing in your custom execution framework. When a DoT tick evaluates damage on a target, it checks the target's active curse bits; if a specific curse bit is present, a health adjustment event is instantly dispatched to the original caster's active ID.

---

## 🏟️ Phase 4: Instance Management & Match-Based Loop Architecture
**Goal:** Establish session states, win/loss evaluation conditions, and endgame economy distribution parameters mirroring competitive modern MOBAs/Arenas.

### 4.1 Arena State Machine Management
*   **Goal:** Orchestrate arena conditions safely across isolated combat regions.
*   **Libraries:** Headless State Frameworks
*   **Deliverables:** Linear phase controller loops processing separate match states sequentially: `WaitingForPlayers` ──► `GateCountdown` ──► `MatchActive` ──► `SuddenDeath` ──► `MatchOver`.

### 4.2 Win / Loss Conditions & Reward Distribution
*   **Goal:** Programmatic evaluation of team configurations and secure communication of results to the persistence gateway.
*   **Libraries:** HTTP POST via Shared Client, `MemoryPack`
*   **Deliverables:** Dynamic team registration bitmasks tracking remaining life states. Upon identifying zero remaining players on a team, the match instance triggers a secure database payload transmission to distribute currency, ladder rating adjustments, or item drops.

---

## 🌍 Phase 5: World Sharding & Multi-Zone Scale Up (MMO Expansion)
*Note: Architectural concepts are high-level and subject to refinement based on production metrics gathered during Phase 4 simulation evaluations.*

### 5.1 Multi-Server Zone Sharding (Headless Architecture scaling)
*   **Goal:** Link 10 or more independent zone application nodes seamlessly, allowing the global environment to expand infinitely without creating single-point hardware failure bottlenecks.
*   **Libraries:** ENet/LiteNetLib cross-talk links, Shared RPC backbones
*   **Deliverables:** Multi-port server distribution routing. Zone 1 runs on `Port 5001`, Zone 2 on `Port 5002`. Nodes stay linked via a private low-latency backend socket network.

### 5.2 Seamless Cross-Instance Server Handoffs
*   **Goal:** Handle player transitions across world zone boundaries automatically without breaking immersion or dropping active inventory caches.
*   **Libraries:** WebSockets or private internal UDP clusters
*   **Deliverables:** Gateway handoff loops. When a player steps onto a boundary trigger coordinate in Zone A, Zone A saves their state, serializes a migration token, pushes it to Zone B, and sends a single small network instruction telling the Unity DOTS client to disconnect from `Port 5001` and seamlessly connect to `Port 5002`.
