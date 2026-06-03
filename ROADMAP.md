# ArenaMMOCore Full Technical Roadmap

This document outlines the engineering architecture and milestone progression for building a high-performance, server-authoritative Action PvP MMORPG. The technical stack focuses on a **Headless .NET Console Server (30Hz)** and a **Unity DOTS/ECS Client**, designed to deliver maximum responsiveness on low-end hardware by achieving flatline memory performance and zero garbage collection (GC) pauses.

---

## 🏗️ Phase 1: The Absolute Core (Networking, Spatial Simulation & Security)
**Goal:** Establish a rock-solid, zero-allocation foundation for client-server communication, movement interpolation, spatial scalability up to 2,000 Concurrent Users (CCU), and server-authoritative anti-cheat protections.

### ✅ 1.1 Automated Shared Data & Serialization
*   **Goal:** Compile common data definitions once and share them instantly between the server and the Burst-compiled Unity client.
*   **Libraries:** `MemoryPack`
*   **Deliverables:** Blittable structs for inputs, spatial states, and RPC events. Zero-allocation `NetworkSerializer` utility class utilizing automated `.csproj` copy pipelines.

### ✅ 1.2 High-Precision 30Hz Simulation Loop
*   **Goal:** Prevent time drift and frame judder across server operating systems without burning 100% CPU thread cores.
*   **Libraries:** `System.Diagnostics.Stopwatch`
*   **Deliverables:** Headless C# console application driven by a tight, spin-locked frame accumulator loop with precise 33.33ms intervals.

### ✅ 1.3 Client-Side Snapshot Interpolation
*   **Goal:** Smooth out the server's 30Hz heartbeat ticks into a fluid 60Hz/144Hz+ visual stream on low-end monitors.
*   **Libraries:** Unity Mathematics (`math.lerp`)
*   **Deliverables:** `SnapshotElement` entity buffer arrays (`IBufferElementData`) and `NetworkMovementSystem` to playback historical points behind an intentional 100ms jitter buffer.

### ⏳ 1.3.5 Dynamic Entity Spawning & Lifecycle Management
*   **Goal:** Efficiently spawn and destroy other players' visible DOTS entities on the client as they move in and out of your network visibility bubble.
*   **Libraries:** Unity Entities (`EntityManager`, `EntityCommandBuffer`)
*   **Deliverables:** A client-side structural registry system. When the `NetworkClientManager` reads a `ServerStatePacket`, it checks if an ECS entity with that `NetworkId` already exists.
    *   **On First Sight:** If the ID is new, the system executes a zero-allocation entity replication step using a `Baking Prefab`, adds the `SnapshotElement` buffers, and prepares it for interpolation.
    *   **On Visibility Lost / Disconnect:** If the server stops sending updates for a specific ID for a set duration (or explicitly sends a disconnect packet), the client uses an `EntityCommandBuffer` to destroy the entity and reclaim memory instantly.

### ⏳ 1.4 Input Prediction & Reconciliation (Micro-Stutter Protection)
*   **Goal:** Provide local players with immediate, lag-free responsiveness while enforcing strict server authority.
*   **Libraries:** `SmoothNet` or custom circular state buffers
*   **Deliverables:** Local input command tracking loops. Server-side validation loops checking predicted states against absolute calculations using a customizable *Permissible Error Threshold* (e.g., <15cm bends, >1m rubber-band snaps).

### ⏳ 1.4.5 Server Input Queuing & Jitter Buffering
*   **Goal:** Prevent a player's character from stuttering on the server due to real-world internet packet jitter or packet bunching over UDP.
*   **Libraries:** Pure C# Struct Collections
*   **Deliverables:** A lightweight, fixed-size input queue array inside each `ServerPlayer` instance. The server reads incoming client inputs, queues them up, and executes exactly 1 input packet per 30Hz tick. If a network hiccup delivers 3 packets at once, the server processes them sequentially over 3 ticks rather than executing a massive teleport jump in a single frame.

### ⏳ 1.5 2D Spatial Grid (Interest Management for 2,000 CCU)
*   **Goal:** Mitigate the $O(N^2)$ networking bandwidth bottleneck by dropping bandwidth throughput from an impossible 38 Gbps to a manageable <1 Gbps.
*   **Libraries:** `NetStack.Aggregation`
*   **Deliverables:** A coordinate-to-cell mapping engine. The server divides worlds into 50x50m grid structures, restricting state packet distribution solely to entities inside a player's direct 9-cell visibility quadrant.

### ⏳ 1.6 Core Anti-Cheating & Input Validation (The "Trust No Client" Engine)
*   **Goal:** Eliminate speed-hacking, teleportation, wall-clipping, and rapid-fire exploits at the socket layer before they reach the game logic.
*   **Libraries:** Pure C# Math, `System.Math`
*   **Deliverables:** Hard boundaries running inside the server input processing loop:
    *   **Movement Vector Validation:** The server tracks a player's maximum allowed distance per tick ($Speed \times \Delta t$). If the incoming input packet moves the player further than this allowance (plus a tiny latency buffer), the movement is truncated or rejected, triggering a mandatory rubber-band snap.
    *   **Time-Dilated Speed Check:** Detect and ban modified client speed-hacks by enforcing an input-to-time tracking variable. If a player submits 45 input frames inside a 1-second server window (where only 30 are mathematically allowed), the server Flags/Disconnects the peer for command throttling.
    *   **Action Attack-Speed Cooldowns:** Skill invocation bits are ignored if the millisecond delta since the last activation is lower than the player's current dynamically calculated `AttackSpeed` state.

---

## 🔐 Phase 2: Session Management, Persistence & Economy Foundations
**Goal:** Handle player authentication, seamless secure handoffs to zone instances, database persistence, and synchronous stat computation via item equipment loops.

### 2.1 Login Server & Match Matchmaking/Lobby Handshake
*   **Goal:** Guard your high-speed simulation nodes from heavy authentication overhead and handle secure zone entry.
*   **Libraries:** ASP.NET Core Web API, `JWT (JSON Web Tokens)`
*   **Deliverables:** A lightweight HTTPS gateway server. Players log in, fetch a cryptographically signed token, query a lobby system, and present that token to the LiteNetLib socket via `ConnectionRequest.AcceptIfKey()`.

### 2.1.5 Network State-Machine & Disconnect Handlers
*   **Goal:** Prevent player duplication and memory leaks ("ghost players") on the server when a socket drops unexpectedly mid-fight.
*   **Libraries:** Core C# Events
*   **Deliverables:** A rigid disconnection sequence handler. When a peer drops, the server locks the state, alerts neighboring cells via the Spatial Grid, saves current metrics asynchronously to the persistence layer, and unregisters the `NetworkId` entirely before cleaning up array allocations.

### 2.2 Entity Persistence Layer & Cache
*   **Goal:** Save and load player character arrays, stats, and equipment positions without stalling the high-frequency network tickers.
*   **Libraries:** PostgreSQL Dapper / Entity Framework Core (Async only), Redis 
*   **Deliverables:** Headless database gateway layer operating strictly on independent asynchronous threads. On login, data maps to an active in-memory `ServerPlayer` structure.

### 2.2.5 Server Static Game Database (The "Item/Skill Dictionary")
*   **Goal:** Prevent the server from ever needing to query SQL/NoSQL databases during active combat loops when reading item lookups or calculating raw modifiers.
*   **Libraries:** Pure C# Read-Only Dictionaries or Flattened Struct Arrays
*   **Deliverables:** A static, in-memory asset registry populated on server boot. It acts as the single source of truth for item attributes, baseline damage scales, skill configurations, and stack rules using raw integer lookups (`ItemDatabase[WeaponID]`).

### 2.3 Zero-Allocation Inventory & Loot Dropping
*   **Goal:** Manage looting actions and inventory storage matrices safely without triggering garbage collection heap spikes.
*   **Libraries:** `MemoryPack`, `NetStack.Serialization`
*   **Deliverables:** Fixed-size item ID collections (`uint[]` arrays mapping to slots) inside the `ServerPlayer` model. Item pickups on the ground represented as minimal mathematical bounding shapes containing unique metadata tags.

### 2.4 Mid-Combat Equipment Swapping, Dynamic Attribute & Skill Granting
*   **Goal:** Allow players to switch loadouts in the middle of a battle, immediately recalculating core combat attributes and toggling active/passive skill visibilities without memory thrashing.
*   **Libraries:** Pure C# Math Structs, Bitwise Flags
*   **Deliverables:** An item-to-modifier look-up calculator running within the shared math layout. Equipped slots are stored as flat item ID structs (`uint WeaponSlotID`, `uint ArmorSlotID`).
    *   **Dynamic Attribute Modification:** Changing an item triggers a full attribute sweep. The server reads the base player stats, adds the raw item modifiers (`MaxHealth`, `AttackSpeed`, `BaseDamage`), and stores the result in a cache-friendly frame layout.
    *   **Item-Granted Skills (Bitmask Toggling):** Items can grant entirely new skills or unlock weapon-specific abilities. The server tracks a player's allowed skill pool using a compact bitmask (`uint AllowedSkillsMask`). Equipping a specific item (e.g., a lightning staff) turns on specific bits in the mask, instantly granting the client access to the "Chain Lightning" spell loop.
    *   **Hot-Bar Validation Safety:** If a player tries to send an input packet casting a skill that their current equipped items do not grant, the server's input validator catches the invalid skill bit mismatch and rejects the action before it updates the simulation frame.

---

# Section 3: Combat Architecture & Systems Validation
Goal: Engineer a deterministic, lag-compensated action combat framework that supports complex spell loops, status effects, and projectile behaviors.

## 3.1 Server-Side Lag Compensation (Hit-Scan Validation)
* **Goal:** Ensure fast-paced projectile and melee accuracy feels completely fair to players operating under volatile latency profiles (e.g., `100ms+` ping).
* **Libraries:** `netcode-lag-compensation` (or raw custom circular structure tracking)
* **Deliverables:** The server caches a `300ms` ring buffer history of all entity spatial coordinates. When a player casts a hit-scan capability, the server winds back the target's positions to the exact millisecond timestamp of the shot to validate the interaction.

### 3.1.5 Universal Server Hitbox Extrusion Engine
* **Goal:** Bridge the gap between a server with no physics engine and a client running a full 3D visual loop.
* **Libraries:** Pure C# Primitive Geometry Math
* **Deliverables:** A lightweight spatial calculation matrix. The server represents characters as basic mathematical 3D bounding cylinders or spheres based on their character model scale (e.g., PoE/Diablo layouts). All melee sweeps or skill cones are evaluated as intersection formula checks between lines, arcs, and circles, completely eliminating PhysX runtime requirements on the server.

---

## 3.2 Dynamic Spatial Projectile Trajectories
* **Goal:** Simulate high-velocity projectile entities across the network without relying on heavy overhead object tracking or Unity physics.
* **Libraries:** Unity Mathematics (`float3`)
* **Deliverables:** Projectiles treated as flat math vectors on the server. Tracking metrics feature:
  * **Pierce:** Simple entity check counters (`HitCount < MaxPierce`).
  * **AoE / Explosion:** Circle-to-point radius proximity validations (`math.distance(projectile, player) <= explosionRadius`).

---

## 3.3 Status Modification Core (Buffs, Debuffs, and Independent Stack Expiration)
* **Goal:** Run thousands of ticks of damage-over-time, attributes, and curses in a cache-friendly entity loop that supports independently disappearing buff/debuff stacks.
* **Libraries:** Fixed-Size Struct Arrays
* **Deliverables:** An optimized status processing array attached directly to the player struct (e.g., maximum of 8 distinct status type slots, with each type supporting up to a maximum of 5 independent stack instances to keep the data layout perfectly blittable).
  * **Independent Stack Ticking:** The server does NOT use a global duration reset for stacks. If a player receives Stack 1 (2-second duration) and 1 second later receives Stack 2 (2-second duration), Stack 1 will cleanly drop off when its individual timer hits 0, causing the client's visible UI stack counter to decrement instantly from 2 to 1.
  * **Zero-Allocation Buff Appending:** When a new stack is applied, the server scans the fixed struct array for the matching `StatusEffectID`, finds the first empty or expired stack sub-slot, resets its specific `TimeRemaining` float, and sets its active state flag to `True`.
  * **State Delta Syncing:** The server packs the dynamic stack counts and remaining durations into raw byte streams, giving the Unity DOTS client precise temporal tracking data to show on the user interface.

---

## 3.4 Synergistic Combat Loops (Lifesteal & Curses)
* **Goal:** Implement complex, intertwined combat interactions (e.g., healing a player through a Damage-over-Time curse applied to an enemy) via high-performance linear execution paths.
* **Libraries:** Core Shared Mathematics
* **Deliverables:** Direct reference passing in your custom execution framework. When a DoT tick evaluates damage on a target, it checks the target's active curse bits; if a specific curse bit is present, a health adjustment event is instantly dispatched to the original caster's active ID.

---

# 🏟️ Phase 4: Instance Management & Match-Based Loop Architecture
**Goal:** Establish session states, win/loss evaluation conditions, and endgame economy distribution parameters mirroring competitive modern MOBAs/Arenas.

## 4.1 Arena State Machine Management
* **Goal:** Orchestrate arena conditions safely across isolated combat regions.
* **Libraries:** Headless State Frameworks
* **Deliverables:** Linear phase controller loops processing separate match states sequentially:
  ```
  WaitingForPlayers ──► GateCountdown ──► MatchActive ──► SuddenDeath ──► MatchOver
  ```

### 4.1.5 Client Scene Orchestrator & UI Bridge
* **Goal:** Synchronize classic Unity visual layers (Canvas UI, screen fades, matchmaking text counters) with the rigid runtime memory of the Burst-compiled DOTS simulation.
* **Libraries:** Unity UI Toolkit or standard Canvas, Unity Entities Bridge
* **Deliverables:** A bidirectional messaging pipeline. When a server packet announces `MatchActive`, a specialized DOTS system triggers a safe main-thread event to drop the visual starting gates and start local match countdown scripts.

## 4.2 Win / Loss Conditions & Reward Distribution
* **Goal:** Programmatic evaluation of team configurations and secure communication of results to the persistence gateway.
* **Libraries:** HTTP POST via Shared Client, MemoryPack
* **Deliverables:** Dynamic team registration bitmasks tracking remaining life states. Upon identifying zero remaining players on a team, the match instance triggers a secure database payload transmission to distribute currency, ladder rating adjustments, or item drops.

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

