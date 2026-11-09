import { describe, expect, it, vi } from "vitest";
import {
  ApiError,
  createApi,
  fetchChange,
  fetchChangeMetrics,
  fetchDocument,
  fetchHistory,
  fetchMetrics,
  REQUEST_HEADER,
} from "./api";
import { fakeServer } from "./testing";

// Design D18: one wrapper for every /api call, the D6 header, and 401 → signed out.
describe("createApi", () => {
  it("asks for JSON and sends the request header on every call, with a JSON body when there is one", async () => {
    const server = fakeServer()
      .on("GET", "/api/members", { status: 200, body: [] })
      .on("POST", "/api/members", { status: 201, body: { id: "m2" } });
    const api = createApi({ fetch: server });

    await api.get("/api/members");
    const added = await api.send<{ id: string }>("POST", "/api/members", {
      email: "sam@example.com",
    });

    expect(added.id).toBe("m2");
    for (const call of server.calls) {
      expect(call.headers["Accept"]).toBe("application/json");
      expect(call.headers[REQUEST_HEADER]).toBe("1");
    }

    const post = server.to("POST", "/api/members")[0];
    expect(post?.headers["Content-Type"]).toBe("application/json");
    expect(post?.body).toEqual({ email: "sam@example.com" });
  });

  it("resolves an empty answer (204) to undefined", async () => {
    const api = createApi({ fetch: fakeServer().on("DELETE", "/api/tokens/t1", { status: 204 }) });

    await expect(api.send("DELETE", "/api/tokens/t1")).resolves.toBeUndefined();
  });

  it("turns a 401 into the signed-out signal and an ApiError", async () => {
    const onSignedOut = vi.fn();
    const api = createApi({
      fetch: fakeServer().on("GET", "/api/changes", { status: 401 }),
      onSignedOut,
    });

    const failure = await api.get("/api/changes").catch((error: unknown) => error);

    expect(onSignedOut).toHaveBeenCalledOnce();
    expect(failure).toBeInstanceOf(ApiError);
    expect((failure as ApiError).status).toBe(401);
  });

  it("rejects other failures with the server's problem detail and reason, without signing out", async () => {
    const onSignedOut = vi.fn();
    const api = createApi({
      fetch: fakeServer().on("POST", "/api/auth/confirm", {
        status: 400,
        body: { detail: "This sign-in link has expired. Ask for a new one.", reason: "expired" },
      }),
      onSignedOut,
    });

    const failure = (await api
      .send("POST", "/api/auth/confirm", { secret: "s" })
      .catch((e: unknown) => e)) as ApiError;

    expect(failure.status).toBe(400);
    expect(failure.message).toBe("This sign-in link has expired. Ask for a new one.");
    expect(failure.reason).toBe("expired");
    expect(onSignedOut).not.toHaveBeenCalled();
  });

  it("falls back to the method, path and status when there's no problem body", async () => {
    const api = createApi({ fetch: () => Promise.resolve(new Response("oops", { status: 500 })) });

    await expect(api.get("/api/changes")).rejects.toThrow("GET /api/changes answered 500");
  });
});

// change-documents-and-history D9: the change page and history helpers.
describe("change page and history helpers", () => {
  it("ask for the change, a document by its encoded path, and history pages", async () => {
    const urls: string[] = [];
    const api = createApi({
      fetch: (input) => {
        urls.push(input);
        return Promise.resolve(new Response("{}", { status: 200 }));
      },
    });

    await fetchChange(api, "c1");
    await fetchDocument(api, "c1", "specs/cli bridge/spec#1.md");
    await fetchHistory(api);
    await fetchHistory(api, "2026-11-08T12:00:00.0000000+00:00");
    await fetchChangeMetrics(api, "c 1");
    await fetchMetrics(api);
    await fetchMetrics(api, "repo=github.com%2Femmz%2Fpanopticon");

    expect(urls).toEqual([
      "/api/changes/c1",
      "/api/changes/c1/documents/specs/cli%20bridge/spec%231.md",
      "/api/history",
      "/api/history?before=2026-11-08T12%3A00%3A00.0000000%2B00%3A00",
      "/api/changes/c%201/metrics",
      "/api/metrics",
      "/api/metrics?repo=github.com%2Femmz%2Fpanopticon",
    ]);
  });

  it("send a 401 to the signed-out signal, like every other call", async () => {
    const onSignedOut = vi.fn();
    const api = createApi({
      fetch: fakeServer()
        .on("GET", "/api/changes/c1", { status: 401 })
        .on("GET", "/api/changes/c1/documents/design.md", { status: 401 })
        .on("GET", "/api/history", { status: 401 }),
      onSignedOut,
    });

    for (const call of [
      fetchChange(api, "c1"),
      fetchDocument(api, "c1", "design.md"),
      fetchHistory(api),
    ]) {
      const failure = await call.catch((error: unknown) => error);
      expect((failure as ApiError).status).toBe(401);
    }

    expect(onSignedOut).toHaveBeenCalledTimes(3);
  });

  it("reject an unknown change with a 404 ApiError, without signing out", async () => {
    const onSignedOut = vi.fn();
    const api = createApi({ fetch: fakeServer(), onSignedOut });

    const failure = (await fetchChange(api, "nope").catch((error: unknown) => error)) as ApiError;

    expect(failure.status).toBe(404);
    expect(onSignedOut).not.toHaveBeenCalled();
  });
});
