package com.cubot.redmanager;

import android.os.Bundle;

import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {
    @Override
    public void onCreate(Bundle savedInstanceState) {
        // Plugins locales (los que estan en este mismo modulo). Los plugins de npm se auto-
        // registran via el generador de Capacitor; los custom hay que registrarlos aca.
        registerPlugin(FuxionCapturePlugin.class);
        super.onCreate(savedInstanceState);
    }
}
