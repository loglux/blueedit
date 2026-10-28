# For those used to make: every target runs tasks.ps1, where the operations are (make help lists them).
#   make check      make ui ARGS="test_lsp.py"      make release ARGS=0.1.3
# On Windows GNU make comes from winget (winget install ezwinports.make, no administrator rights).
TASKS := check test-all ui publish publish-linux packages packages-test installer installer-sandbox images-sandbox sandbox newcomer newcomer-linux dialogs lang-template setup setup-proofing setup-lsp setup-math status runner pr merge release
.PHONY: help $(TASKS)

help $(TASKS):
	@pwsh -NoProfile -File tasks.ps1 $@ $(ARGS)
