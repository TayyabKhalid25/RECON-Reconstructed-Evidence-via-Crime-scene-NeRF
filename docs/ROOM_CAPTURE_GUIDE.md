# Large Room Capture Protocol

Capturing a large environment (like a classroom or an entire office) requires a fundamentally different approach than capturing a single object on a table. Gaussian Splatting and NeRFs rely on **parallax** (moving the camera physically through space) to understand depth. 

If a room capture "melts" or falls apart when you move through the 3D scene, it is almost always due to improper camera trajectories during recording.

## Core Principles

1. **Parallax is King:** You must physically move the camera side-to-side or up-and-down relative to the objects you are filming. Walking straight forward down a hallway or aisle provides almost zero parallax and will ruin the depth estimation.
2. **Never Stand and Spin:** Standing in one spot and panning the camera like a panorama is the #1 reason room reconstructions fail. Your feet must always be moving. If you need to turn around, walk in a wide arc while turning.
3. **Overlapping Coverage:** The software needs to see the same visual features from multiple different angles to triangulate their position in 3D space. 

---

## The "Outside-In" Strategy for Rooms

For a large room, you cannot just walk aimlessly. You must follow a systematic pattern to ensure the camera connects all the geometry together.

### Phase 1: The Perimeter Loop
Start by mapping the outer shell of the room. 
- Walk along the edges of the room with your back close to the walls. 
- Point your camera **inwards** toward the center of the room at roughly a 45-degree angle to the wall.
- Complete a full, continuous loop around the entire room. This acts as the "anchor" that ties all the walls and corners together.

### Phase 2: The "Lawnmower" Grid
Now you must map the interior space and objects (like desks, chairs, and tables).
- Walk up and down the room in a zig-zag grid pattern (like mowing a lawn).
- **Crucial:** Keep the camera pointed at an angle (e.g., 45 degrees left or right) as you walk, rather than straight ahead. This ensures the camera is sliding *past* the objects, providing the necessary side-to-side parallax.

### Phase 3: Vertical Variation
A scene captured entirely at eye-level will look great at eye-level, but will collapse if the viewer crouches or looks down in the 3D app.
- For important areas (e.g., the specific area where the crime evidence is located), perform a second pass.
- In this pass, hold the phone significantly lower (waist height) or higher (above your head) and angle it towards the subject.

---

## The Enemies of Reconstruction

When capturing rooms, be extremely careful around these common failure points:

### 1. Repeating Patterns (e.g., Classrooms)
Identical chairs or desks lined up in perfect rows confuse the AI. It will match the leg of Chair A in frame 1 to the leg of Chair B in frame 2, folding the room in half.
- **Solution:** Break the repetition. Place random unique items (a jacket, a brightly colored backpack, a distinct piece of paper) on various chairs or desks before filming to give the AI unique visual anchors.

### 2. Thin Structures
Chair legs, thin desk frames, and wires are incredibly hard to reconstruct. From a distance, they blend into the background.
- **Solution:** If a thin structure is critical to the evidence, you must walk very close to it and orbit it specifically to ensure the camera gets dense pixel coverage.

### 3. Blank White Walls
The software needs texture to track movement. A perfectly smooth, featureless white wall is invisible to the AI.
- **Solution:** Tape post-it notes, pieces of tape, or markers to blank walls before filming. 

### 4. Mirrors and Reflections
A mirror shows a different image depending on where you stand, violating the core assumption of photogrammetry (that a point in space looks the same from all angles).
- **Solution:** Avoid them if possible, or expect the mirror and whatever is reflected in it to look like a blurry, chaotic mess in the final splat.

---

## The Printed Marker (Scale & Origin)

Even in a massive room, you still only need **one** printed marker! 
- Place the 170mm printed marker flat on the floor or a table in a central, well-lit location.
- **Get Close:** The accuracy of the room's metric scale depends entirely on how clearly the AI can measure that marker. At least once during your capture, walk close enough to the marker so that it fills a large portion of your camera screen, then slowly back away.
