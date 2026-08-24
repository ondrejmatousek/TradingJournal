(() => {
    const page = document.querySelector(".cot-page");
    const form = document.querySelector("#cot-update-form");
    const loading = document.querySelector(".cot-loading");
    if (!page || !form || !loading) return;

    const url = new URL(window.location.href);
    if (url.searchParams.get("refreshed") === "1") {
        url.searchParams.delete("refreshed");
        window.history.replaceState({}, document.title, url.toString());
        loading.remove();
        return;
    }

    let refreshing = false;
    const refresh = async event => {
        event?.preventDefault();
        if (refreshing) return;
        refreshing = true;
        page.classList.add("is-refreshing");
        loading.hidden = false;
        form.querySelector("button").disabled = true;

        try {
            const response = await fetch(form.action, {
                method: "POST",
                body: new FormData(form),
                credentials: "same-origin",
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });
            if (!response.ok) throw new Error(`COT refresh failed (${response.status})`);

            const refreshedUrl = new URL(window.location.href);
            refreshedUrl.searchParams.set("refreshed", "1");
            window.location.replace(refreshedUrl.toString());
        } catch (error) {
            console.error(error);
            refreshing = false;
            page.classList.remove("is-refreshing");
            loading.hidden = true;
            form.querySelector("button").disabled = false;
        }
    };

    form.addEventListener("submit", refresh);
    refresh();
})();
