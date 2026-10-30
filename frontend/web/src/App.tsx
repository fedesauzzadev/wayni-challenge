import { Building2, FileUp, Landmark, Users } from "lucide-react";
import { ArchivoPage } from "@/pages/ArchivoPage";
import { DeudoresPage } from "@/pages/DeudoresPage";
import { EntidadesPage } from "@/pages/EntidadesPage";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Toaster } from "@/components/ui/sonner";

export default function App() {
  return (
    <div className="mx-auto min-h-dvh max-w-6xl px-6 py-10">
      <header className="mb-8 flex flex-wrap items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <div className="flex size-11 items-center justify-center rounded-xl bg-primary/15 text-primary">
            <Landmark className="size-6" />
          </div>
          <div>
            <h1 className="text-xl font-semibold tracking-tight">BCRA — Deudores financieros</h1>
            <p className="text-sm text-muted-foreground">
              Administración del pipeline: cargas, deudores y entidades
            </p>
          </div>
        </div>
        <a
          href="http://localhost:8025"
          target="_blank"
          rel="noreferrer"
          className="text-xs text-muted-foreground underline-offset-4 hover:text-foreground hover:underline"
        >
          Mailpit ↗
        </a>
      </header>

      <main>
        <Tabs defaultValue="archivo">
          <TabsList className="flex h-auto w-full flex-wrap justify-start gap-1">
            <TabsTrigger value="archivo">
              <FileUp className="size-4" />
              Archivo
            </TabsTrigger>
            <TabsTrigger value="deudores">
              <Users className="size-4" />
              Deudores
            </TabsTrigger>
            <TabsTrigger value="entidades">
              <Building2 className="size-4" />
              Entidades
            </TabsTrigger>
          </TabsList>
          <TabsContent value="archivo">
            <ArchivoPage />
          </TabsContent>
          <TabsContent value="deudores">
            <DeudoresPage />
          </TabsContent>
          <TabsContent value="entidades">
            <EntidadesPage />
          </TabsContent>
        </Tabs>
      </main>

      <Toaster />
    </div>
  );
}
