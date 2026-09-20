/*
	The little the browser has to remember on its own: which colour scheme, which panels are open.
	Everything else is server state. localStorage may be missing (private mode, blocked storage),
	so every call swallows the failure and the app carries on with its defaults.
*/
window.taroking = {
	getPref(key) {
		try {
			return localStorage.getItem(key);
		} catch {
			return null;
		}
	},

	setPref(key, value) {
		try {
			localStorage.setItem(key, value);
		} catch {
			/* nothing to do — the choice lasts for this page only */
		}
	},

	setTheme(name) {
		document.documentElement.dataset.theme = name;
		this.setPref("tk-theme", name);
	}
};
