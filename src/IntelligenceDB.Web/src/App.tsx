import { Link, Route, Routes } from "react-router";
import { AppLayout } from "@/components/layout/AppLayout";
import { CURRENT_PHASE, modules } from "@/components/layout/modules";
import { LoginPage } from "@/features/auth/LoginPage";
import { ComingInPhasePage } from "@/features/ComingInPhasePage";
import { DashboardPage } from "@/features/dashboard/DashboardPage";
import { ServerDetailPage } from "@/features/servers/ServerDetailPage";
import { ServersPage } from "@/features/servers/ServersPage";
import { SettingsPage } from "@/features/settings/SettingsPage";

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<AppLayout />}>
        <Route index element={<DashboardPage />} />
        <Route path="servers" element={<ServersPage />} />
        <Route path="servers/:id" element={<ServerDetailPage />} />
        <Route path="settings" element={<SettingsPage />} />
        {modules
          .filter((m) => m.phase > CURRENT_PHASE)
          .map((m) => (
            <Route key={m.path} path={m.path.slice(1)} element={<ComingInPhasePage module={m} />} />
          ))}
        <Route
          path="*"
          element={
            <div className="pt-10 text-center">
              <p className="text-lg font-medium">Page not found</p>
              <Link to="/" className="text-sm text-primary hover:underline">
                Back to the dashboard
              </Link>
            </div>
          }
        />
      </Route>
    </Routes>
  );
}
