#!/bin/bash
#
# post-edit-reminder.sh
#
# RETIRED — Both checks have moved to frozen-file-check.sh (PreToolUse)
# so they fire BEFORE the edit with calm reminders, rather than after
# the edit with alarming "undo what you did" language.
#
# Moved checks:
#   1. Frozen artifact detection → frozen-file-check.sh check 1
#   2. Test weakening detection  → frozen-file-check.sh check 2
#
# This file is kept as documentation. It is no longer registered in
# settings.json and will not fire.
#
exit 0
