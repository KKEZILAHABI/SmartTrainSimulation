# Smart Train Simulation

Collaborative project built with **Blender**, **Unity 2022.3.62f3 LTS**, **MATLAB** and **Git + Git LFS**.

This README explains how to install the correct Unity version, set up the repo, and work together without conflicts or broken builds.

---

## 📌 Project Info

- **Unity:** 2022.3.62f3 LTS (everyone must use this version)
- **Source Control:** Git + Git LFS
- **Team Size:** 5

> ❗ Do NOT upgrade Unity unless the whole team agrees.

---

# 🟢 1. Install Unity 2022.3.62f3 LTS

### Step 1 — Install Unity Hub
Download and install:
```bash
https://unity.com/download
```

Sign in (or create) a Unity account.

---

### Step 2 — Install the correct Unity version

1. Open **Unity Hub**
2. Go to **Installs → Install Editor**
3. Open the **LTS** tab
4. Install **2022.3.62f3 LTS**
5. Add modules:
   - Windows Build Support
   - Micrososft Visual Studio Community 2022

> If Unity Hub offers to “upgrade project”, **cancel** and ask the team.

---

# 🔧 2. Git & Git LFS (everyone)

### Install Git(Kama huna)
```bash
https://git-scm.com/downloads
```

### Install Git LFS (only once per computer)
```bash
https://git-lfs.github.com/
```
Then run:

```bash
git lfs install
```
---
# 🚀 3. FIRST-TIME SETUP (When you clone the project)

### 1️⃣ Clone

Use VSCode GUI or

```bash
git clone https://github.com/KKEZILAHABI/SmartTrainSimulation

cd SmartTrainSimulation
```
### 2️⃣ Verify LFS is active
```bash
git lfs env
```
### 3️⃣ Open project in Unity
 - Open Unity Hub
 - Click Open
 - Select the project folder
 - Wait — Unity will rebuild the Library/ folder automatically

---

# 4. WORKFLOW

### ✅ A) What to do on EVERY new work session (subsequent pulls)

- Pull → Open → Work → Test → Commit small → Push

---

### ✍️ B) While working

- Keep commits small and clear:

---

# 🚫 5. Do NOT Commit

Already ignored by .gitignore, but remember:

- Library/
- Temp/
- Build/ / Builds/
- Logs
- Local user settings
- Generated solution files

And:

❌ No builds
❌ No temporary exports
❌ No personal Unity config

---

# 🔒 6. Team Rules

❗ Avoid two people editing the scene at a time
- ✔️ Use prefabs for shared objects
- ✔️ Pull before starting work
- ✔️ Test before pushing
- ❌ Don’t change Unity version alone
- 💬 Communicate big changes early

---