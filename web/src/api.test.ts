import { describe, expect, it, vi } from "vitest";
import { ApiError, createApi, REQUEST_HEADER } from "./api";
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
