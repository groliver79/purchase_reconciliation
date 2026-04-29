# Git Cheat Sheet

> Quick reference for daily Git commands — works in both Git Bash & PowerShell

---

## Setup & Config

> One-time configuration when getting started

| Command | Description |
| -------- | ------------- |
| `git config --global user.name "Your Name"` | Set your display name for commits |
| `git config --global user.email "you@email.com"` | Set your email for commits |
| `git config --list` | View all current Git config settings |
| `git clone <url>` | Copy a remote repo to your machine |

---

## 🔄 Daily Workflow

> The commands you will use every single day

| Command | Description |
| -------- | ------------- |
| `git status` | See what files are changed, staged, or untracked |
| `git add .` | Stage ALL changed files for commit |
| `git add <file>` | Stage one specific file only |
| `git commit -m "message"` | Save staged changes with a description |
| `git push` | Upload commits to GitHub |
| `git pull` | Download latest changes from GitHub |

---

## 🌿 Branches

| Command | Description |
| -------- | ------------- |
| `git branch` | List all local branches |
| `git branch <name>` | Create a new branch |
| `git checkout <name>` | Switch to an existing branch |
| `git checkout -b <name>` | Create AND switch to a new branch |
| `git merge <branch>` | Merge a branch into your current one |
| `git branch -d <name>` | Delete a branch after merging |

---

## ☁️ Remote / GitHub

| Command | Description |
| -------- | ------------- |
| `git remote -v` | Show connected remote repos |
| `git push -u origin <branch>` | Push a new branch to GitHub for the first time |
| `git fetch` | Check for remote changes without merging |
| `git pull origin <branch>` | Pull a specific branch from GitHub |

---

## ↩️ Undo & Fix

| Command | Description |
| -------- | ------------- |
| `git restore <file>` | Discard unsaved changes to a file ⚠️ cannot be undone |
| `git restore --staged <file>` | Unstage a file but keep the changes |
| `git commit --amend -m "new message"` | Fix the last commit message (only before pushing!) |
| `git revert <hash>` | Safely undo a commit by creating a new one |

---

## 🔍 Inspect & Compare

| Command | Description |
| -------- | ------------- |
| `git log` | Full commit history |
| `git log --oneline` | Compact one-line commit history |
| `git diff` | Show unstaged changes line by line |
| `git diff --staged` | Show staged changes before committing |
| `git show <hash>` | See details of a specific commit |

---

## ⚡ Your Daily Loop

```bash
git pull  →  edit files  →  git status  →  git add .  →  git commit -m "..."  →  git push
```

1. **Start of day** — always `git pull` before touching anything
2. **Make your changes** — edit files in VS Code / Visual Studio
3. **Check your work** — `git status` to see what changed
4. **Stage** — `git add .` to prepare files for commit
5. **Commit** — `git commit -m "brief description of what you did"`
6. **Push** — `git push` to send your work up to GitHub

---

## 💡 Git Bash vs PowerShell

All Git commands above are **identical** in both terminals. The only difference is how you navigate folders:

|                    | Git Bash                | PowerShell                |
| -------------------| ------------------------| --------------------------|
| Navigate to folder | `cd /c/Users/Name/repos`| `cd C:\Users\Name\repos`  |
| List files         | `ls`                    | `ls` or `dir`             |
| Git commands       | Same                    | Same                      |

---

## 📝 Good Commit Message Tips

- ✅ `Fix bug in allocation lookup filter`
- ✅ `Add validation to purchase request form`
- ✅ `Update README with setup instructions`
- ❌ `fix`
- ❌ `changes`
- ❌ `asdfgh`

> **Rule of thumb:** Write your message as if completing the sentence *"This commit will..."*
