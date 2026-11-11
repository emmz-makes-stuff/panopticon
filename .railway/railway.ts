import { defineRailway, project, service } from "railway/iac";
// This repository manages only its own resources in the environment. Other
// repositories export their own partial name.
// See https://docs.railway.com/infrastructure-as-code#multi-repo-projects
export const partial = "panopticon";
export default defineRailway(() => {
  const panopticon = service("panopticon", {
    build: { buildEnvironment: "V3", builder: "DOCKERFILE", dockerfilePath: "Dockerfile" },
    healthcheck: "/healthz",
    healthcheckTimeout: 120,
    preDeploy: "dotnet /app/Panopticon.Server.dll --migrate",
    replicas: { "us-east4-eqdc4a": 1 },
  });

  return project("panopticon", {
    variables: { managed: false },
    resources: [panopticon],
  });
});
