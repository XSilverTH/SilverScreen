#include <gtk/gtk.h>
#include <webkit/webkit.h>
#include <libsoup/soup.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static const char *LOGIN_URI =
    "https://accounts.google.com/ServiceLogin?service=youtube&continue=https%3A%2F%2Fwww.youtube.com%2F";

static const char *USER_AGENT =
    "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36";

typedef struct {
    WebKitCookieManager *cookie_manager;
    WebKitWebView *web_view;
    gboolean checking;
    gboolean check_pending;
    gboolean load_finished;
    guint debounce_timer_id;
} AuthState;

static gboolean is_youtube_domain(const char *domain) {
    if (!domain || domain[0] == '\0') return FALSE;
    const char *host = (domain[0] == '.') ? domain + 1 : domain;
    if (g_ascii_strcasecmp(host, "youtube.com") == 0) return TRUE;
    size_t len = strlen(host);
    const char suffix[] = ".youtube.com";
    size_t suffix_len = sizeof(suffix) - 1;
    if (len > suffix_len && g_ascii_strcasecmp(host + (len - suffix_len), suffix) == 0) {
        return TRUE;
    }
    return FALSE;
}

static gboolean is_auth_cookie(const char *name) {
    if (!name) return FALSE;
    return (g_ascii_strcasecmp(name, "SAPISID") == 0 ||
            g_ascii_strcasecmp(name, "__Secure-3PAPISID") == 0);
}

static gboolean is_companion_cookie(const char *name) {
    if (!name) return FALSE;
    return (g_ascii_strcasecmp(name, "LOGIN_INFO") == 0 ||
            g_ascii_strcasecmp(name, "SID") == 0 ||
            g_ascii_strcasecmp(name, "SSID") == 0 ||
            g_ascii_strcasecmp(name, "HSID") == 0 ||
            g_ascii_strcasecmp(name, "__Secure-1PSID") == 0 ||
            g_ascii_strcasecmp(name, "__Secure-3PSID") == 0);
}

static void on_get_cookies_finish(GObject *source, GAsyncResult *result, gpointer user_data);
static void schedule_cookie_check(AuthState *state, guint delay_ms);

static gboolean on_debounce_timeout(gpointer user_data) {
    AuthState *state = (AuthState *)user_data;
    state->debounce_timer_id = 0;
    if (state->checking) {
        state->check_pending = TRUE;
        return G_SOURCE_REMOVE;
    }
    state->checking = TRUE;
    webkit_cookie_manager_get_cookies(
        state->cookie_manager,
        "https://www.youtube.com/",
        NULL,
        on_get_cookies_finish,
        state);
    return G_SOURCE_REMOVE;
}

static void schedule_cookie_check(AuthState *state, guint delay_ms) {
    if (state->debounce_timer_id != 0) {
        g_source_remove(state->debounce_timer_id);
    }
    state->debounce_timer_id = g_timeout_add(delay_ms, on_debounce_timeout, state);
}

static void on_get_cookies_finish(GObject *source, GAsyncResult *result, gpointer user_data) {
    AuthState *state = (AuthState *)user_data;
    state->checking = FALSE;

    GError *error = NULL;
    GList *cookies = webkit_cookie_manager_get_cookies_finish(
        WEBKIT_COOKIE_MANAGER(source), result, &error);

    if (error) {
        g_error_free(error);
        if (state->check_pending) {
            state->check_pending = FALSE;
            schedule_cookie_check(state, 100);
        }
        return;
    }

    gboolean has_auth = FALSE;
    gboolean has_companion = FALSE;

    for (GList *l = cookies; l != NULL; l = l->next) {
        SoupCookie *c = (SoupCookie *)l->data;
        const char *domain = soup_cookie_get_domain(c);
        if (!is_youtube_domain(domain)) continue;

        const char *name = soup_cookie_get_name(c);
        const char *value = soup_cookie_get_value(c);
        if (!name || !value || value[0] == '\0') continue;

        if (is_auth_cookie(name)) has_auth = TRUE;
        if (is_companion_cookie(name)) has_companion = TRUE;
    }

    if (has_auth && (has_companion || state->load_finished)) {
        GString *out = g_string_new("# Netscape HTTP Cookie File\n");

        for (GList *l = cookies; l != NULL; l = l->next) {
            SoupCookie *c = (SoupCookie *)l->data;
            const char *domain = soup_cookie_get_domain(c);
            if (!is_youtube_domain(domain)) continue;

            const char *name = soup_cookie_get_name(c);
            const char *value = soup_cookie_get_value(c);
            const char *path = soup_cookie_get_path(c);
            if (!name || !value || !domain) continue;

            if (strpbrk(name, "\t\r\n") || strpbrk(value, "\t\r\n") ||
                strpbrk(domain, "\t\r\n") || (path && strpbrk(path, "\t\r\n"))) {
                continue;
            }

            if (!path || path[0] == '\0') path = "/";

            if (soup_cookie_get_http_only(c)) {
                g_string_append(out, "#HttpOnly_");
            }
            g_string_append(out, domain);
            g_string_append_c(out, '\t');
            g_string_append(out, (domain[0] == '.') ? "TRUE" : "FALSE");
            g_string_append_c(out, '\t');
            g_string_append(out, path);
            g_string_append_c(out, '\t');
            g_string_append(out, soup_cookie_get_secure(c) ? "TRUE" : "FALSE");
            g_string_append_c(out, '\t');

            GDateTime *expires = soup_cookie_get_expires(c);
            gint64 exp_unix = expires ? g_date_time_to_unix(expires) : 0;
            if (exp_unix < 0) exp_unix = 0;
            g_string_append_printf(out, "%" G_GINT64_FORMAT "\t", exp_unix);

            g_string_append(out, name);
            g_string_append_c(out, '\t');
            g_string_append(out, value);
            g_string_append_c(out, '\n');
        }

        fputs(out->str, stdout);
        fflush(stdout);
        g_string_free(out, TRUE);
        g_list_free_full(cookies, (GDestroyNotify)soup_cookie_free);
        exit(0);
    }

    g_list_free_full(cookies, (GDestroyNotify)soup_cookie_free);

    if (has_auth && !has_companion && !state->load_finished) {
        schedule_cookie_check(state, 500);
    } else if (state->check_pending) {
        state->check_pending = FALSE;
        schedule_cookie_check(state, 100);
    }
}

static void on_cookie_changed(WebKitCookieManager *cm, gpointer user_data) {
    (void)cm;
    AuthState *state = (AuthState *)user_data;
    schedule_cookie_check(state, 150);
}

static void on_load_changed(WebKitWebView *wv, WebKitLoadEvent event, gpointer user_data) {
    (void)wv;
    AuthState *state = (AuthState *)user_data;
    if (event == WEBKIT_LOAD_FINISHED) {
        state->load_finished = TRUE;
        schedule_cookie_check(state, 100);
    } else if (event == WEBKIT_LOAD_STARTED) {
        state->load_finished = FALSE;
    }
}

static gboolean on_window_close_request(GtkWindow *window, gpointer user_data) {
    (void)window;
    (void)user_data;
    exit(1);
    return TRUE;
}

static gboolean on_key_pressed(
    GtkEventControllerKey *controller,
    guint keyval,
    guint keycode,
    GdkModifierType state,
    gpointer user_data)
{
    (void)controller;
    (void)keycode;
    (void)state;
    (void)user_data;
    if (keyval == GDK_KEY_Escape) {
        exit(1);
    }
    return FALSE;
}

int run_auth_helper(void) {
    if (!gtk_init_check()) {
        fprintf(stderr, "SilverScreen: failed to initialize GTK for authentication worker\n");
        return 1;
    }

    GtkWidget *window = gtk_window_new();
    gtk_window_set_title(GTK_WINDOW(window), "Sign in to YouTube");
    gtk_window_set_default_size(GTK_WINDOW(window), 960, 720);

    WebKitNetworkSession *session = webkit_network_session_new_ephemeral();
    WebKitCookieManager *cookie_manager = webkit_network_session_get_cookie_manager(session);
    WebKitWebView *web_view = WEBKIT_WEB_VIEW(
        g_object_new(WEBKIT_TYPE_WEB_VIEW, "network-session", session, NULL));

    gtk_widget_set_hexpand(GTK_WIDGET(web_view), TRUE);
    gtk_widget_set_vexpand(GTK_WIDGET(web_view), TRUE);

    WebKitSettings *settings = webkit_web_view_get_settings(web_view);
    webkit_settings_set_user_agent(settings, USER_AGENT);

    gtk_window_set_child(GTK_WINDOW(window), GTK_WIDGET(web_view));

    AuthState *state = g_new0(AuthState, 1);
    state->cookie_manager = cookie_manager;
    state->web_view = web_view;

    g_signal_connect(window, "close-request", G_CALLBACK(on_window_close_request), NULL);

    GtkEventController *key_controller = gtk_event_controller_key_new();
    g_signal_connect(key_controller, "key-pressed", G_CALLBACK(on_key_pressed), NULL);
    gtk_widget_add_controller(GTK_WIDGET(window), key_controller);

    g_signal_connect(cookie_manager, "changed", G_CALLBACK(on_cookie_changed), state);
    g_signal_connect(web_view, "load-changed", G_CALLBACK(on_load_changed), state);

    webkit_web_view_load_uri(web_view, LOGIN_URI);
    gtk_window_present(GTK_WINDOW(window));

    GMainLoop *loop = g_main_loop_new(NULL, FALSE);
    g_main_loop_run(loop);
    return 1;
}
